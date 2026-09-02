using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.App.Services;
using TimecodeBridge.Services.Interfaces;

namespace TimecodeBridge.App.ViewModels;

/// <summary>
/// メインウィンドウのViewModel（macOS版）。Windows版と同一のプロジェクト管理・Undo/Redoを提供する。
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    public const string ProjectFileFilter = "TimecodeBridge2 プロジェクト (*.json)|*.json|すべてのファイル (*.*)|*.*";

    private readonly IProjectService _projectService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IRecentProjectsService _recentProjectsService;
    private readonly ICueManager _cueManager;
    private readonly IHostRegistry _hostRegistry;
    private readonly ITimecodeRelay _timecodeRelay;
    private readonly ITimecodeEngine _timecodeEngine;
    private readonly IOscTriggerPanelManager _oscTriggerPanelManager;
    private bool _isNewProject = true;

    [ObservableProperty]
    private string _title = "TimecodeBridge2";

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private IReadOnlyList<string> _recentProjects = [];

    /// <summary>ステータスバー表示用のプロジェクト名（未保存マーク付き）。</summary>
    [ObservableProperty]
    private string _projectDisplayName = "未保存の新規プロジェクト";

    /// <summary>ステータスバーのツールチップ用フルパス。</summary>
    [ObservableProperty]
    private string? _projectFilePath;

    /// <summary>発火時オートミュート機能のマスタースイッチ（メニュー「キュー設定」から切替）。</summary>
    public bool AutoMuteEnabled => _cueManager.IsAutoMuteEnabled;

    [RelayCommand]
    private void ToggleAutoMute()
    {
        _cueManager.IsAutoMuteEnabled = !_cueManager.IsAutoMuteEnabled;
        OnPropertyChanged(nameof(AutoMuteEnabled));
        _projectService.MarkAsChanged();
    }

    // Child ViewModels
    public TimecodeViewModel TimecodeViewModel { get; }
    public CueListViewModel CueListViewModel { get; }
    public RelayViewModel RelayViewModel { get; }
    public HostManagerViewModel HostManagerViewModel { get; }
    public OscTriggerPanelViewModel OscTriggerPanelViewModel { get; }
    public LogViewModel LogViewModel { get; }

    public MainViewModel(
        IProjectService projectService,
        IFileDialogService fileDialogService,
        IRecentProjectsService recentProjectsService,
        ICueManager cueManager,
        IHostRegistry hostRegistry,
        ITimecodeRelay timecodeRelay,
        ITimecodeEngine timecodeEngine,
        IOscTriggerPanelManager oscTriggerPanelManager,
        TimecodeViewModel timecodeViewModel,
        CueListViewModel cueListViewModel,
        RelayViewModel relayViewModel,
        HostManagerViewModel hostManagerViewModel,
        OscTriggerPanelViewModel oscTriggerPanelViewModel,
        LogViewModel logViewModel)
    {
        _projectService = projectService;
        _fileDialogService = fileDialogService;
        _recentProjectsService = recentProjectsService;
        _cueManager = cueManager;
        _hostRegistry = hostRegistry;
        _timecodeRelay = timecodeRelay;
        _timecodeEngine = timecodeEngine;
        _oscTriggerPanelManager = oscTriggerPanelManager;

        TimecodeViewModel = timecodeViewModel;
        CueListViewModel = cueListViewModel;
        RelayViewModel = relayViewModel;
        HostManagerViewModel = hostManagerViewModel;
        OscTriggerPanelViewModel = oscTriggerPanelViewModel;
        LogViewModel = logViewModel;

        RecentProjects = _recentProjectsService.GetRecentProjects();
        _projectService.UnsavedChangesStatusChanged += OnUnsavedChangesStatusChanged;
        _projectService.ChangeCommitted += OnChangeCommitted;

        RecordBaseline();
    }

    // --- Undo/Redo（プロジェクトデータのスナップショット履歴） ---
    // ソース設定（デバイス・ジェネレーター）はUndo対象外。
    // 対象にすると取り消しのたびに受信/生成が停止・再開してライブを乱すため

    private const int MaxHistory = 50;
    private readonly List<string> _history = [];  // JSON直列化＝ProjectDataの深いコピー
    private int _historyIndex = -1;
    private bool _applyingHistory;
    private DateTime _lastSnapshotAt;

    public bool CanUndo => _historyIndex > 0;
    public bool CanRedo => _historyIndex < _history.Count - 1;

    private string CaptureSnapshot()
    {
        var data = new ProjectData
        {
            Cues = _cueManager.Cues.ToList(),
            Hosts = _hostRegistry.Hosts.ToList(),
            RelaySettings = new RelaySettings
            {
                OscAddressPattern = _timecodeRelay.OscAddressPattern,
                ContinuousInterval = _timecodeRelay.ContinuousInterval,
                TargetHostIds = _timecodeRelay.TargetHostIds.ToList(),
                IsContinuousEnabled = _timecodeRelay.IsContinuousEnabled,
            },
            Offset = _timecodeEngine.Offset,
            OscTriggerPanel = _oscTriggerPanelManager.GetSettings(),
            CueSync = TimecodeViewModel.CueSync.GetSettings(),
            CueAutoMuteEnabled = _cueManager.IsAutoMuteEnabled,
        };
        return JsonSerializer.Serialize(data, ProjectData.CreateJsonOptions());
    }

    /// <summary>履歴を現在状態1点へ仕切り直す（新規・読込時）。</summary>
    private void RecordBaseline()
    {
        _history.Clear();
        _history.Add(CaptureSnapshot());
        _historyIndex = 0;
        NotifyHistoryChanged();
    }

    private void OnChangeCommitted(object? sender, EventArgs e)
    {
        if (_applyingHistory) return;

        // Undo後に新しい編集をしたら、その先のRedo履歴は破棄
        if (_historyIndex < _history.Count - 1)
        {
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        }

        var snapshot = CaptureSnapshot();

        // スライダードラッグ等の連続変更は直近スナップショットへ集約する
        if (_historyIndex > 0 && (DateTime.UtcNow - _lastSnapshotAt).TotalMilliseconds < 500)
        {
            _history[_historyIndex] = snapshot;
        }
        else
        {
            _history.Add(snapshot);
            _historyIndex++;
            if (_history.Count > MaxHistory)
            {
                _history.RemoveAt(0);
                _historyIndex--;
            }
        }

        _lastSnapshotAt = DateTime.UtcNow;
        NotifyHistoryChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        _historyIndex--;
        ApplySnapshot(_history[_historyIndex]);
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        _historyIndex++;
        ApplySnapshot(_history[_historyIndex]);
    }

    private void ApplySnapshot(string json)
    {
        var data = JsonSerializer.Deserialize<ProjectData>(json, ProjectData.CreateJsonOptions())!;

        _applyingHistory = true;
        try
        {
            // 受信は止めない。適用中の中間状態でキューが発火しないよう一時ミュート＋位置仕切り直し
            var wasMuted = _cueManager.IsMuted;
            _cueManager.IsMuted = true;
            try
            {
                _cueManager.ResetTracking();

                foreach (var cue in _cueManager.Cues.ToList()) _cueManager.RemoveCue(cue.Id);
                foreach (var host in _hostRegistry.Hosts.ToList()) _hostRegistry.RemoveHost(host.Id);
                foreach (var cue in data.Cues) _cueManager.AddCue(cue);
                foreach (var host in data.Hosts) _hostRegistry.AddHost(host);

                _timecodeRelay.OscAddressPattern = data.RelaySettings.OscAddressPattern;
                _timecodeRelay.ContinuousInterval = data.RelaySettings.ContinuousInterval;
                _timecodeRelay.TargetHostIds = data.RelaySettings.TargetHostIds;
                _timecodeRelay.IsContinuousEnabled = data.RelaySettings.IsContinuousEnabled;

                _timecodeEngine.Offset = data.Offset;
                _oscTriggerPanelManager.LoadSettings(data.OscTriggerPanel);
                TimecodeViewModel.CueSync.LoadSettings(data.CueSync ?? new CueSyncSettings());
                _cueManager.IsAutoMuteEnabled = data.CueAutoMuteEnabled;
            }
            finally
            {
                _cueManager.IsMuted = wasMuted;
            }

            TimecodeViewModel.SyncOffsetFromEngine();
            CueListViewModel.SyncFromService();
            RelayViewModel.SyncFromService();
            OscTriggerPanelViewModel.SyncFromService();
            OnPropertyChanged(nameof(AutoMuteEnabled));

            // Undo/Redo後は保存済みファイルと異なる可能性が高いためdirty扱いにする
            _projectService.MarkAsChanged();
        }
        finally
        {
            _applyingHistory = false;
        }

        NotifyHistoryChanged();
    }

    private void NotifyHistoryChanged()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void NewProject()
    {
        if (!ConfirmDiscardIfDirty()) return;

        ClearAllData();

        // Reset relay settings
        _timecodeRelay.OscAddressPattern = "/timecode";
        _timecodeRelay.ContinuousInterval = new RelayInterval(RelayIntervalMode.EveryFrame, 0);
        _timecodeRelay.TargetHostIds = [];
        _timecodeRelay.IsContinuousEnabled = false;

        // Reset engine offset
        _timecodeEngine.Offset = TimecodeOffset.Zero(_timecodeEngine.FrameRate);
        TimecodeViewModel.SyncOffsetFromEngine();

        // Reset source settings
        TimecodeViewModel.RestoreSourceSettings(new TimecodeSourceSettings());
        TimecodeViewModel.CueSync.LoadSettings(new CueSyncSettings());
        _cueManager.IsAutoMuteEnabled = true;
        OnPropertyChanged(nameof(AutoMuteEnabled));

        // Sync child ViewModels
        CueListViewModel.SyncFromService();
        RelayViewModel.SyncFromService();
        OscTriggerPanelViewModel.SyncFromService();

        // 保存先パスと未保存フラグをクリア（旧プロジェクトへの誤上書きを防ぐ）
        _projectService.Reset();

        _isNewProject = true;
        UpdateTitle();
        RecordBaseline();
    }

    [RelayCommand]
    private void SaveProject(string? filePath)
    {
        if (filePath is not null)
        {
            SaveToPath(filePath);
            return;
        }

        TrySaveWithPrompt();
    }

    /// <summary>
    /// 現在の保存先へ保存する。保存先が未確定なら保存ダイアログを出す。
    /// 保存が完了したら true、ユーザーがキャンセルしたら false。
    /// </summary>
    public bool TrySaveWithPrompt()
    {
        var path = _projectService.CurrentFilePath
            ?? _fileDialogService.ShowSaveFileDialog(ProjectFileFilter, "project.json");
        if (path is null) return false;

        SaveToPath(path);
        return true;
    }

    [RelayCommand]
    private void SaveProjectAs(string? filePath)
    {
        var path = filePath
            ?? _fileDialogService.ShowSaveFileDialog(ProjectFileFilter, "project.json");
        if (path is null) return;

        SaveToPath(path);
    }

    /// <summary>ファイル選択ダイアログを開いてプロジェクトを読み込む（メニュー用）。</summary>
    [RelayCommand]
    private void OpenProjectWithDialog()
    {
        var filePath = _fileDialogService.ShowOpenFileDialog(ProjectFileFilter);
        if (filePath is null) return;

        OpenProject(filePath);
    }

    [RelayCommand]
    private void OpenProject(string filePath)
    {
        if (!ConfirmDiscardIfDirty()) return;

        ProjectData data;
        try
        {
            data = _projectService.LoadProject(filePath);
        }
        catch (Exception ex)
        {
            NotifyLoadError(filePath, ex);
            return;
        }

        _isNewProject = false;

        // MRUリストに追加（ProjectServiceの責務外）
        _recentProjectsService.AddRecentProject(filePath);

        ClearAllData();

        // Restore cues（ID重複データはAddCueが例外を投げるため先に除去）
        foreach (var cue in data.Cues.DistinctBy(c => c.Id))
        {
            _cueManager.AddCue(cue);
        }

        // Restore hosts
        foreach (var host in data.Hosts.DistinctBy(h => h.Id))
        {
            _hostRegistry.AddHost(host);
        }

        // Restore relay settings
        _timecodeRelay.OscAddressPattern = data.RelaySettings.OscAddressPattern;
        _timecodeRelay.ContinuousInterval = data.RelaySettings.ContinuousInterval;
        _timecodeRelay.TargetHostIds = data.RelaySettings.TargetHostIds;
        _timecodeRelay.IsContinuousEnabled = data.RelaySettings.IsContinuousEnabled;

        // Restore engine offset
        _timecodeEngine.Offset = data.Offset;
        TimecodeViewModel.SyncOffsetFromEngine();

        // Restore source settings
        TimecodeViewModel.RestoreSourceSettings(data.SourceSettings);

        // Restore OSC trigger panel
        _oscTriggerPanelManager.LoadSettings(data.OscTriggerPanel);

        // Restore Cue-Sync settings
        TimecodeViewModel.CueSync.LoadSettings(data.CueSync);

        // Restore auto-mute master switch
        _cueManager.IsAutoMuteEnabled = data.CueAutoMuteEnabled;
        OnPropertyChanged(nameof(AutoMuteEnabled));

        // Sync child ViewModels
        CueListViewModel.SyncFromService();
        RelayViewModel.SyncFromService();
        OscTriggerPanelViewModel.SyncFromService();

        UpdateTitle();
        RecentProjects = _recentProjectsService.GetRecentProjects();
        RecordBaseline();
    }

    /// <summary>未保存の変更を破棄してよいか確認する。テスト時に差し替え可能。</summary>
    protected virtual bool ConfirmDiscardIfDirty()
    {
        if (!_projectService.HasUnsavedChanges) return true;

        return ModalDialog.Confirm("確認", "未保存の変更があります。破棄して続行しますか？");
    }

    /// <summary>プロジェクト読み込み失敗の通知。テスト時に差し替え可能。</summary>
    protected virtual void NotifyLoadError(string filePath, Exception ex)
    {
        ModalDialog.ShowMessage("読み込みエラー",
            $"プロジェクトを開けませんでした:\n{filePath}\n\n{ex.Message}");
    }

    private void ClearAllData()
    {
        // 読み込み途中の中間状態でキュー発火・リレー送信が走らないよう、先に受信を止める
        _timecodeEngine.Stop();

        // 前プロジェクトの再生位置が残ると、読込直後の最初のフレームで中間キューが一斉発火する
        _cueManager.ResetTracking();

        foreach (var cue in _cueManager.Cues.ToList())
        {
            _cueManager.RemoveCue(cue.Id);
        }

        foreach (var host in _hostRegistry.Hosts.ToList())
        {
            _hostRegistry.RemoveHost(host.Id);
        }

        _oscTriggerPanelManager.Clear();
    }

    private void SaveToPath(string filePath)
    {
        var data = new ProjectData
        {
            Cues = _cueManager.Cues.ToList(),
            Hosts = _hostRegistry.Hosts.ToList(),
            RelaySettings = new RelaySettings
            {
                OscAddressPattern = _timecodeRelay.OscAddressPattern,
                ContinuousInterval = _timecodeRelay.ContinuousInterval,
                TargetHostIds = _timecodeRelay.TargetHostIds.ToList(),
                IsContinuousEnabled = _timecodeRelay.IsContinuousEnabled,
            },
            Offset = _timecodeEngine.Offset,
            SourceSettings = TimecodeViewModel.GetSourceSettings(),
            OscTriggerPanel = _oscTriggerPanelManager.GetSettings(),
            CueSync = TimecodeViewModel.CueSync.GetSettings(),
            CueAutoMuteEnabled = _cueManager.IsAutoMuteEnabled,
        };

        _isNewProject = false;
        _projectService.SaveProject(filePath, data);

        // MRUリストに追加（ProjectServiceの責務外）
        _recentProjectsService.AddRecentProject(filePath);

        UpdateTitle();
        RecentProjects = _recentProjectsService.GetRecentProjects();
    }

    private void OnUnsavedChangesStatusChanged(object? sender, EventArgs e)
    {
        HasUnsavedChanges = _projectService.HasUnsavedChanges;
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        var title = "TimecodeBridge2";
        var currentPath = _isNewProject ? null : _projectService.CurrentFilePath;
        var displayName = currentPath is null
            ? "未保存の新規プロジェクト"
            : System.IO.Path.GetFileName(currentPath);

        if (currentPath is not null)
        {
            title = $"TimecodeBridge2 - {displayName}";
        }

        if (_projectService.HasUnsavedChanges)
        {
            title += " *";
            displayName += " *";
        }

        Title = title;
        ProjectDisplayName = displayName;
        ProjectFilePath = currentPath;
    }

    public void Dispose()
    {
        _projectService.UnsavedChangesStatusChanged -= OnUnsavedChangesStatusChanged;
        _projectService.ChangeCommitted -= OnChangeCommitted;
    }
}
