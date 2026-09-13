using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.Host.Services;

namespace TimecodeBridge.Host.Bridge;

/// <summary>
/// プロジェクト(キュー・ホスト・ポン出し・CueSync・オフセット・ソース設定)の新規/開く/保存と、
/// 編集履歴(Undo/Redo)を担う。v2 の MainViewModel と同じ方針: 履歴は ProjectData の JSON スナップショット、
/// ソース設定(デバイス・生成器)は Undo 対象外(取り消しのたびに受信が止まるとライブを乱す)。
/// </summary>
public sealed class ProjectCoordinator : IProjectHistory
{
    public const string FileExtension = "json";
    private const int MaxHistory = 50;
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(500);

    private readonly HostState _state;
    private readonly ITimecodeEngine _engine;
    private readonly ICueManager _cues;
    private readonly IHostRegistry _hosts;
    private readonly IOscTriggerPanelManager _panel;
    private readonly IProjectService _project;
    private readonly RecentProjectsStore _recent;

    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    private bool _applying;
    private DateTime _lastSnapshotAt;

    public ProjectCoordinator(HostState state, ITimecodeEngine engine, ICueManager cues, IHostRegistry hosts,
        IOscTriggerPanelManager panel, IProjectService project, RecentProjectsStore recent)
    {
        _state = state; _engine = engine; _cues = cues; _hosts = hosts; _panel = panel; _project = project; _recent = recent;
        _state.History = this;
        RecordBaseline();
    }

    /// <summary>ファイルダイアログと確認ダイアログの親。MainWindow が設定する。</summary>
    public Func<Window?> OwnerProvider { get; set; } = () => null;

    public bool CanUndo => _historyIndex > 0;
    public bool CanRedo => _historyIndex < _history.Count - 1;

    // ---- 編集の確定 ----------------------------------------------------------------

    /// <summary>プロジェクトに影響する変更のあとに呼ぶ。未保存フラグを立て、履歴へ積む。</summary>
    public void Commit()
    {
        if (_applying) return;
        _project.MarkAsChanged();

        if (_historyIndex < _history.Count - 1)
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);

        var snapshot = Serialize(Capture());
        // スライダー操作などの連続変更は直近スナップショットへまとめる
        if (_historyIndex > 0 && DateTime.UtcNow - _lastSnapshotAt < CoalesceWindow)
        {
            _history[_historyIndex] = snapshot;
        }
        else
        {
            _history.Add(snapshot);
            _historyIndex++;
            if (_history.Count > MaxHistory) { _history.RemoveAt(0); _historyIndex--; }
        }
        _lastSnapshotAt = DateTime.UtcNow;
        _state.MarkDirty(Domain.Project);
    }

    public bool Undo()
    {
        if (!CanUndo) return false;
        _historyIndex--;
        ApplySnapshot(_history[_historyIndex]);
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo) return false;
        _historyIndex++;
        ApplySnapshot(_history[_historyIndex]);
        return true;
    }

    private void RecordBaseline()
    {
        _history.Clear();
        _history.Add(Serialize(Capture()));
        _historyIndex = 0;
        _lastSnapshotAt = DateTime.MinValue;
    }

    private void ApplySnapshot(string json)
    {
        var data = JsonSerializer.Deserialize<ProjectData>(json, ProjectData.CreateJsonOptions())!;
        _applying = true;
        try
        {
            // 受信は止めない。適用中の中間状態でキューが発火しないよう一時ミュートし、位置を仕切り直す
            var wasMuted = _cues.IsMuted;
            _cues.IsMuted = true;
            try
            {
                ApplyData(data, restoreSource: false);
                _cues.ResetTracking();
            }
            finally
            {
                _cues.IsMuted = wasMuted;
            }
            _project.MarkAsChanged();
        }
        finally
        {
            _applying = false;
        }
        _state.MarkDirty(Domain.All);
    }

    // ---- 新規 / 開く / 保存 ------------------------------------------------------------

    public async Task<bool> NewAsync()
    {
        if (!await ConfirmDiscardIfDirtyAsync()) return false;
        ClearAll();
        _engine.Offset = TimecodeOffset.Zero(_engine.FrameRate);
        _state.CueSync.OscAddress = new CueSyncSettings().OscAddress;
        _state.CueSync.TargetHostIds.Clear();
        _cues.IsAutoMuteEnabled = true;
        _project.Reset();
        RecordBaseline();
        _state.MarkDirty(Domain.All);
        return true;
    }

    /// <summary>path が null ならダイアログで選ぶ。戻り値は (キャンセルされたか, 開いたパス)。</summary>
    public async Task<(bool Cancelled, string? Path)> OpenAsync(string? path)
    {
        if (!await ConfirmDiscardIfDirtyAsync()) return (true, null);
        path ??= await PickOpenPathAsync();
        if (path is null) return (true, null);

        ProjectData data;
        try
        {
            data = _project.LoadProject(path);
        }
        catch (FileNotFoundException)
        {
            _recent.Remove(path);
            throw;
        }
        ClearAll();
        ApplyData(data, restoreSource: true);
        RecordBaseline();
        _recent.Add(path);
        _state.MarkDirty(Domain.All);
        return (false, path);
    }

    public async Task<(bool Cancelled, string? Path)> SaveAsync(bool saveAs, string? suggestedName)
    {
        var path = saveAs ? null : _project.CurrentFilePath;
        path ??= await PickSavePathAsync(suggestedName ?? Path.GetFileName(_project.CurrentFilePath) ?? "project");
        if (path is null) return (true, null);
        _project.SaveProject(path, Capture(includeSource: true));
        _recent.Add(path);
        _state.MarkDirty(Domain.Project);
        return (false, path);
    }

    private async Task<bool> ConfirmDiscardIfDirtyAsync()
    {
        if (!_project.HasUnsavedChanges) return true;
        var owner = OwnerProvider();
        if (owner is null) return true;
        return await ConfirmDialog.ShowAsync(owner, "確認", "未保存の変更があります。破棄して続行しますか？");
    }

    private async Task<string?> PickOpenPathAsync()
    {
        var owner = OwnerProvider();
        if (owner is null) return null;
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "プロジェクトを開く",
            AllowMultiple = false,
            FileTypeFilter = new[] { ProjectFileType },
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async Task<string?> PickSavePathAsync(string suggestedName)
    {
        var owner = OwnerProvider();
        if (owner is null) return null;
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "プロジェクトを保存",
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName),
            DefaultExtension = FileExtension,
            FileTypeChoices = new[] { ProjectFileType },
        });
        return file?.TryGetLocalPath();
    }

    private static readonly FilePickerFileType ProjectFileType = new("TimecodeBridge プロジェクト") { Patterns = new[] { "*." + FileExtension } };

    // ---- データの収集と適用 --------------------------------------------------------------

    public ProjectData Capture(bool includeSource = false)
    {
        var data = new ProjectData
        {
            Cues = _cues.Cues.ToList(),
            Hosts = _hosts.Hosts.ToList(),
            Offset = _engine.Offset,
            OscTriggerPanel = _panel.GetSettings(),
            CueSync = new CueSyncSettings { OscAddress = _state.CueSync.OscAddress, TargetHostIds = _state.CueSync.TargetHostIds.ToList() },
            CueAutoMuteEnabled = _cues.IsAutoMuteEnabled,
        };
        if (includeSource)
        {
            data.SourceSettings = new TimecodeSourceSettings
            {
                SourceType = _state.Mode == "generate" ? TimecodeSourceType.Generator : TimecodeSourceType.Ltc,
                DeviceId = _state.SelectedInputDeviceId ?? string.Empty,
                GeneratorSettings = new GeneratorSettings
                {
                    FrameRate = _state.Generator.FrameRate,
                    StartTime = _state.Generator.StartTime,
                    OutputDeviceId = _state.Generator.OutputDeviceId,
                    VolumeLevel = _state.Generator.VolumeLevel,
                },
                FreerunDurationSeconds = _engine.FreerunDurationSeconds,
            };
        }
        return data;
    }

    private void ClearAll()
    {
        // 読み込み途中の中間状態でキュー発火・中継送信が走らないよう、位置を仕切り直してから消す
        _cues.ResetTracking();
        foreach (var cue in _cues.Cues.ToList()) _cues.RemoveCue(cue.Id);
        foreach (var host in _hosts.Hosts.ToList()) _hosts.RemoveHost(host.Id);
        _panel.Clear();
    }

    private void ApplyData(ProjectData data, bool restoreSource)
    {
        // ID 重複データは AddCue が例外を投げるため先に除去
        foreach (var cue in _cues.Cues.ToList()) _cues.RemoveCue(cue.Id);
        foreach (var host in _hosts.Hosts.ToList()) _hosts.RemoveHost(host.Id);
        foreach (var cue in data.Cues.DistinctBy(c => c.Id)) _cues.AddCue(cue);
        foreach (var host in data.Hosts.DistinctBy(h => h.Id)) _hosts.AddHost(host);

        _engine.Offset = data.Offset;
        _panel.LoadSettings(data.OscTriggerPanel);
        _state.CueSync.OscAddress = data.CueSync.OscAddress;
        _state.CueSync.TargetHostIds.Clear();
        _state.CueSync.TargetHostIds.AddRange(data.CueSync.TargetHostIds);
        _cues.IsAutoMuteEnabled = data.CueAutoMuteEnabled;

        if (restoreSource) RestoreSource(data.SourceSettings);
    }

    private void RestoreSource(TimecodeSourceSettings source)
    {
        _engine.Stop();
        _state.LtcStarted = false;
        _state.GeneratorRunning = false;
        _state.Mode = source.SourceType == TimecodeSourceType.Generator ? "generate" : "ltc";
        _state.Generator.FrameRate = source.GeneratorSettings.FrameRate;
        _state.Generator.StartTime = source.GeneratorSettings.StartTime;
        _state.Generator.OutputDeviceId = source.GeneratorSettings.OutputDeviceId;
        _state.Generator.VolumeLevel = source.GeneratorSettings.VolumeLevel;
        _engine.FreerunDurationSeconds = source.FreerunDurationSeconds;
        _state.SelectedInputDeviceId = string.IsNullOrEmpty(source.DeviceId) ? null : source.DeviceId;

        // v2 と同じく、LTC モードでデバイスが残っていれば受信を再開する
        if (_state.Mode == "ltc" && _state.FindDevice(_state.SelectedInputDeviceId) is { } device)
        {
            try
            {
                _engine.StartLtc(device.Id, device.IsLoopback);
                _state.LtcStarted = true;
                _state.SetError(null);
            }
            catch (Exception ex)
            {
                _state.SetError($"音声入力を開けません: {ex.Message}");
            }
        }
    }

    private static string Serialize(ProjectData data) => JsonSerializer.Serialize(data, ProjectData.CreateJsonOptions());
}

public interface IProjectHistory
{
    bool CanUndo { get; }
    bool CanRedo { get; }
}
