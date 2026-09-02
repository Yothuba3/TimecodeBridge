using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimecodeBridge.Models;
using TimecodeBridge.Services.Interfaces;

namespace TimecodeBridge.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    public const string ProjectFileFilter = "TimecodeBridge プロジェクト (*.json)|*.json|すべてのファイル (*.*)|*.*";

    private readonly IProjectService _projectService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IRecentProjectsService _recentProjectsService;
    private readonly ICueManager _cueManager;
    private readonly IHostRegistry _hostRegistry;
    private readonly ITimecodeRelay _timecodeRelay;
    private readonly ITimecodeEngine _timecodeEngine;
    private readonly TimecodeViewModel _timecodeViewModel;
    private readonly CueListViewModel _cueListViewModel;
    private readonly RelayViewModel _relayViewModel;
    private readonly IOscTriggerPanelManager _oscTriggerPanelManager;
    private readonly OscTriggerPanelViewModel _oscTriggerPanelViewModel;
    private bool _isNewProject = true;

    [ObservableProperty]
    private string _title = "TimecodeBridge";

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

    public MainViewModel(
        IProjectService projectService,
        IRecentProjectsService recentProjectsService,
        ICueManager cueManager,
        IHostRegistry hostRegistry,
        ITimecodeRelay timecodeRelay,
        ITimecodeEngine timecodeEngine,
        TimecodeViewModel timecodeViewModel,
        CueListViewModel cueListViewModel,
        RelayViewModel relayViewModel,
        IOscTriggerPanelManager oscTriggerPanelManager,
        OscTriggerPanelViewModel oscTriggerPanelViewModel,
        IFileDialogService fileDialogService)
    {
        _projectService = projectService;
        _fileDialogService = fileDialogService;
        _recentProjectsService = recentProjectsService;
        _cueManager = cueManager;
        _hostRegistry = hostRegistry;
        _timecodeRelay = timecodeRelay;
        _timecodeEngine = timecodeEngine;
        _timecodeViewModel = timecodeViewModel;
        _cueListViewModel = cueListViewModel;
        _relayViewModel = relayViewModel;
        _oscTriggerPanelManager = oscTriggerPanelManager;
        _oscTriggerPanelViewModel = oscTriggerPanelViewModel;

        RecentProjects = _recentProjectsService.GetRecentProjects();
        _projectService.UnsavedChangesStatusChanged += OnUnsavedChangesStatusChanged;
        _projectService.ChangeCommitted += OnChangeCommitted;

        RecordBaseline();
    }

    // --- Undo/Redo（プロジェクトデータのスナップショット履歴） ---
    // ponytail: ソース設定（デバイス・ジェネレーター）はUndo対象外。
    // 対象にすると取り消しのたびに受信/生成が停止・再開してライブを乱すため

    private const int MaxHistory = 50;
    private readonly List<string> _history = [];  // JSON直列化＝ProjectDataの深いコピー
    private int _historyIndex = -1;
    private bool _applyingHistory;
    private DateTime _lastSnapshotAt;

    public bool CanUndo => _historyIndex > 0;
    public bool CanRedo => _historyIndex < _history.Count - 1;

    /// <summary>発火時オートミュート機能のマスタースイッチ（メニュー「キュー設定」から切替）。</summary>
    public bool AutoMuteEnabled => _cueManager.IsAutoMuteEnabled;

    [RelayCommand]
    private void ToggleAutoMute()
    {
        _cueManager.IsAutoMuteEnabled = !_cueManager.IsAutoMuteEnabled;
        OnPropertyChanged(nameof(AutoMuteEnabled));
        _projectService.MarkAsChanged();
    }

    /// <summary>信号喪失時の自動復帰（デコーダ・ゲートのリセット）のマスタースイッチ。</summary>
    public bool LtcAutoRecoverEnabled => _timecodeEngine.LtcAutoRecoverOnSignalLoss;

    [RelayCommand]
    private void ToggleLtcAutoRecover()
    {
        _timecodeEngine.LtcAutoRecoverOnSignalLoss = !_timecodeEngine.LtcAutoRecoverOnSignalLoss;
        OnPropertyChanged(nameof(LtcAutoRecoverEnabled));
        _projectService.MarkAsChanged();
    }

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
            CueSync = _timecodeViewModel.CueSync.GetSettings(),
            CueAutoMuteEnabled = _cueManager.IsAutoMuteEnabled,
            LtcAutoRecoverEnabled = _timecodeEngine.LtcAutoRecoverOnSignalLoss,
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
                _cueManager.IsAutoMuteEnabled = data.CueAutoMuteEnabled;
                _timecodeEngine.LtcAutoRecoverOnSignalLoss = data.LtcAutoRecoverEnabled;
                _oscTriggerPanelManager.LoadSettings(data.OscTriggerPanel);
                _timecodeViewModel.CueSync.LoadSettings(data.CueSync ?? new CueSyncSettings());
            }
            finally
            {
                _cueManager.IsMuted = wasMuted;
            }

            _timecodeViewModel.SyncOffsetFromEngine();
            _cueListViewModel.SyncFromService();
            _relayViewModel.SyncFromService();
            _oscTriggerPanelViewModel.SyncFromService();

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
        _cueManager.IsAutoMuteEnabled = true;
        _timecodeEngine.LtcAutoRecoverOnSignalLoss = true;
        _timecodeViewModel.SyncOffsetFromEngine();

        // Reset source settings
        _timecodeViewModel.RestoreSourceSettings(new TimecodeSourceSettings());
        _timecodeViewModel.CueSync.LoadSettings(new CueSyncSettings());

        // Sync child ViewModels
        _cueListViewModel.SyncFromService();
        _relayViewModel.SyncFromService();
        _oscTriggerPanelViewModel.SyncFromService();

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
    private void SaveProjectAs(string filePath)
    {
        SaveToPath(filePath);
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
        _cueManager.IsAutoMuteEnabled = data.CueAutoMuteEnabled;
        _timecodeEngine.LtcAutoRecoverOnSignalLoss = data.LtcAutoRecoverEnabled;
        _timecodeViewModel.SyncOffsetFromEngine();

        // Restore source settings
        _timecodeViewModel.RestoreSourceSettings(data.SourceSettings);

        // Restore OSC trigger panel
        _oscTriggerPanelManager.LoadSettings(data.OscTriggerPanel);

        // Restore Cue-Sync settings
        _timecodeViewModel.CueSync.LoadSettings(data.CueSync);

        // Sync child ViewModels
        _cueListViewModel.SyncFromService();
        _relayViewModel.SyncFromService();
        _oscTriggerPanelViewModel.SyncFromService();

        UpdateTitle();
        RecentProjects = _recentProjectsService.GetRecentProjects();
        RecordBaseline();
    }

    /// <summary>未保存の変更を破棄してよいか確認する。テスト時に差し替え可能。</summary>
    protected virtual bool ConfirmDiscardIfDirty()
    {
        if (!_projectService.HasUnsavedChanges) return true;

        var result = System.Windows.MessageBox.Show(
            "未保存の変更があります。破棄して続行しますか？",
            "確認", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning);
        return result == System.Windows.MessageBoxResult.OK;
    }

    /// <summary>プロジェクト読み込み失敗の通知。テスト時に差し替え可能。</summary>
    protected virtual void NotifyLoadError(string filePath, Exception ex)
    {
        System.Windows.MessageBox.Show(
            $"プロジェクトを開けませんでした:\n{filePath}\n\n{ex.Message}",
            "読み込みエラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
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
            SourceSettings = _timecodeViewModel.GetSourceSettings(),
            OscTriggerPanel = _oscTriggerPanelManager.GetSettings(),
            CueSync = _timecodeViewModel.CueSync.GetSettings(),
            CueAutoMuteEnabled = _cueManager.IsAutoMuteEnabled,
            LtcAutoRecoverEnabled = _timecodeEngine.LtcAutoRecoverOnSignalLoss,
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
        var title = "TimecodeBridge";
        var projectName = "未保存の新規プロジェクト";
        string? currentPath = null;

        if (!_isNewProject)
        {
            currentPath = _projectService.CurrentFilePath;
            if (currentPath is not null)
            {
                var fileName = System.IO.Path.GetFileName(currentPath);
                title = $"TimecodeBridge - {fileName}";
                projectName = fileName;
            }
        }

        if (_projectService.HasUnsavedChanges)
        {
            title += " *";
            projectName += " *";
        }

        Title = title;
        ProjectDisplayName = projectName;
        ProjectFilePath = currentPath;
    }

    public void Dispose()
    {
        _projectService.UnsavedChangesStatusChanged -= OnUnsavedChangesStatusChanged;
        _projectService.ChangeCommitted -= OnChangeCommitted;
    }
}
