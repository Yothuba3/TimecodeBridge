using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
namespace TimecodeBridge.Tests.ViewModels;

using TimecodeBridge.Core.Models;
using TimecodeBridge.Services;
using TimecodeBridge.Services.Interfaces;
using TimecodeBridge.ViewModels;

// --- Stubs ---

internal class StubProjectService : IProjectService
{
    private bool _hasUnsavedChanges;
    private ProjectData? _lastSavedData;
    private string? _lastSavedPath;

    public string? CurrentFilePath { get; set; }
    public bool HasUnsavedChanges => _hasUnsavedChanges;

    public ProjectData? LastSavedData => _lastSavedData;
    public string? LastSavedPath => _lastSavedPath;

    public event EventHandler<EventArgs>? UnsavedChangesStatusChanged;
    public event EventHandler<EventArgs>? ChangeCommitted;

    public ProjectData? ProjectDataToLoad { get; set; }

    public ProjectData LoadProject(string filePath)
    {
        var data = ProjectDataToLoad ?? new ProjectData();
        CurrentFilePath = filePath;
        SetHasUnsavedChanges(false);
        return data;
    }

    public void SaveProject(string filePath, ProjectData data)
    {
        _lastSavedPath = filePath;
        _lastSavedData = data;
        CurrentFilePath = filePath;
        SetHasUnsavedChanges(false);
    }

    public void MarkAsChanged()
    {
        SetHasUnsavedChanges(true);
        ChangeCommitted?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        CurrentFilePath = null;
        SetHasUnsavedChanges(false);
    }

    public void SimulateUnsavedChanges(bool value)
    {
        SetHasUnsavedChanges(value);
    }

    private void SetHasUnsavedChanges(bool value)
    {
        if (_hasUnsavedChanges == value) return;
        _hasUnsavedChanges = value;
        UnsavedChangesStatusChanged?.Invoke(this, EventArgs.Empty);
    }
}

internal class StubRecentProjectsService : IRecentProjectsService
{
    private readonly List<string> _recentProjects = [];

    public IReadOnlyList<string> GetRecentProjects() => _recentProjects.AsReadOnly();

    public void AddRecentProject(string filePath)
    {
        _recentProjects.Remove(filePath);
        _recentProjects.Insert(0, filePath);
    }
}

internal class StubAppSettingsService : IAppSettingsService
{
    public List<string> LoadRecentProjects() => [];
    public void SaveRecentProjects(List<string> projects) { }
}

internal class StubCueManagerForMain : ICueManager
{
    private readonly List<Cue> _cues = [];

    public IReadOnlyList<Cue> Cues => _cues.AsReadOnly();
    public int TriggerWindowFrames { get; set; } = 3;
    public bool IsMuted { get; set; }

    public event EventHandler<CueTriggeredEventArgs>? CueTriggered;

    public void AddCue(Cue cue) => _cues.Add(cue);
    public void UpdateCue(string cueId, Cue updatedCue)
    {
        var index = _cues.FindIndex(c => c.Id == cueId);
        if (index >= 0) _cues[index] = updatedCue;
    }
    public void RemoveCue(string cueId) => _cues.RemoveAll(c => c.Id == cueId);
    public void ReorderCues(IReadOnlyList<string> orderedCueIds) { }
    public void SetCueEnabled(string cueId, bool enabled) { }
    public void ManualTrigger(string cueId) { }
    public void ResetTracking() { }
    public void SendCueSync(string oscAddress, IReadOnlyList<string> targetHostIds) { }


    public void ClearAll()
    {
        _cues.Clear();
    }
}

internal class StubHostRegistryForMain : IHostRegistry
{
    private readonly List<OscHost> _hosts = [];

    public IReadOnlyList<OscHost> Hosts => _hosts.AsReadOnly();

    public event EventHandler<HostChangedEventArgs>? HostChanged;

    public void AddHost(OscHost host)
    {
        _hosts.Add(host);
        HostChanged?.Invoke(this, new HostChangedEventArgs
        {
            HostId = host.Id,
            ChangeType = HostChangeType.Added,
        });
    }

    public void UpdateHost(string hostId, OscHost updatedHost)
    {
        var index = _hosts.FindIndex(h => h.Id == hostId);
        if (index >= 0) _hosts[index] = updatedHost;
    }

    public void RemoveHost(string hostId)
    {
        _hosts.RemoveAll(h => h.Id == hostId);
        HostChanged?.Invoke(this, new HostChangedEventArgs
        {
            HostId = hostId,
            ChangeType = HostChangeType.Removed,
        });
    }

    public void SetHostEnabled(string hostId, bool enabled) { }

    public IReadOnlyList<OscHost> GetEnabledHosts(IReadOnlyList<string> hostIds) =>
        _hosts.Where(h => hostIds.Contains(h.Id) && h.IsEnabled).ToList().AsReadOnly();

    public void ClearAll()
    {
        _hosts.Clear();
    }
}

internal class StubTimecodeRelayForMain : ITimecodeRelay
{
    public string OscAddressPattern { get; set; } = "/timecode";
    public RelayInterval ContinuousInterval { get; set; } = new(RelayIntervalMode.EveryFrame, 0);
    public IReadOnlyList<string> TargetHostIds { get; set; } = [];
    public bool IsContinuousEnabled { get; set; }

    public void TriggerOneShot() { }
}

internal class StubCueDialogServiceForMain : ICueDialogService
{
    public Cue? ShowEditDialog(Cue template, IReadOnlyList<OscHost> hosts, FrameRate frameRate, string title) => template;
    public CueBatchEditResult? ShowBatchEditDialog(int cueCount, IReadOnlyList<OscHost> hosts, FrameRate frameRate) => null;
    public (int Count, TimeSpan Interval)? ShowBatchDuplicateDialog() => null;
}

internal class StubTimecodeEngineForMain : ITimecodeEngine
{
    public TimecodeValue CurrentRawTimecode { get; set; }
    public TimecodeValue CurrentOffsetTimecode { get; set; }
    public TimecodeOffset Offset { get; set; } = TimecodeOffset.Zero(FrameRate.Fps30);
    public FrameRate FrameRate { get; set; } = FrameRate.Fps30;
    public TimecodeSourceType ActiveSource => TimecodeSourceType.Ltc;
    public bool IsReceiving => false;
    public double FreerunDurationSeconds { get; set; }
    public bool IsFreerunning => false;

    public void StartLtc(string audioDeviceId, bool isLoopback = false) { }
    public void Stop() { }
    public void StartGenerator(GeneratorSettings settings) { }
    public void ResetGenerator() { }
    public void ResetGenerator(TimecodeValue startTime) { }
    public void ResumeGenerator() { }
    public void StopGenerator() { }

    public event EventHandler<TimecodeUpdatedEventArgs>? TimecodeUpdated;
    public event EventHandler<TimecodeStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<AudioSamplesEventArgs>? AudioSamplesAvailable;
}

internal class StubOscSenderForMain : IOscSender
{
    public void Send(string oscAddress, IReadOnlyList<OscArgument> arguments, IReadOnlyList<string> targetHostIds) { }
    public void SendPing(string hostId) { }
    public Task SendIcmpPingAsync(string hostId, int framesPerSecond) => Task.CompletedTask;
    public event EventHandler<OscSendResultEventArgs>? SendCompleted;
}

internal class StubOscTriggerDialogServiceForMain : IOscTriggerDialogService
{
    public OscTriggerEditResult ShowEditDialog(OscTriggerButton template, IReadOnlyList<OscHost> hosts, string title, bool canDelete)
        => new(OscTriggerEditAction.Save, template);
}

internal class StubFileDialogServiceForMain : IFileDialogService
{
    public string? SaveDialogResult { get; set; }
    public string? ShowOpenFileDialog(string filter, string? initialDirectory = null) => null;
    public string? ShowSaveFileDialog(string filter, string? defaultFileName = null, string? initialDirectory = null) => SaveDialogResult;
}

// --- Tests ---

public class MainViewModelTests
{
    private readonly StubProjectService _projectService = new();
    private readonly StubRecentProjectsService _recentProjectsService = new();
    private readonly StubCueManagerForMain _cueManager = new();
    private readonly StubHostRegistryForMain _hostRegistry = new();
    private readonly StubTimecodeRelayForMain _timecodeRelay = new();
    private readonly StubTimecodeEngineForMain _timecodeEngine = new();
    private readonly TimecodeViewModel _timecodeViewModel;
    private readonly CueListViewModel _cueListViewModel;
    private readonly RelayViewModel _relayViewModel;
    private readonly OscTriggerPanelManager _oscTriggerPanelManager;
    private readonly OscTriggerPanelViewModel _oscTriggerPanelViewModel;

    public MainViewModelTests()
    {
        _timecodeViewModel = new TimecodeViewModel(_timecodeEngine, _cueManager, _projectService,
            new CueSyncViewModel(_cueManager, _hostRegistry, _projectService));
        _cueListViewModel = new CueListViewModel(_cueManager, _timecodeEngine, _hostRegistry, new StubCueDialogServiceForMain(), _projectService);
        _relayViewModel = new RelayViewModel(_timecodeRelay, _hostRegistry, _projectService);
        _oscTriggerPanelManager = new OscTriggerPanelManager(new StubOscSenderForMain(), _hostRegistry);
        _oscTriggerPanelViewModel = new OscTriggerPanelViewModel(
            _oscTriggerPanelManager, new StubOscTriggerDialogServiceForMain(), _hostRegistry, _projectService);
    }

    // 確認ダイアログ(MessageBox)をヘッドレス環境で表示しないテスト用サブクラス
    private class TestMainViewModel(
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
        : MainViewModel(projectService, recentProjectsService, cueManager, hostRegistry,
            timecodeRelay, timecodeEngine, timecodeViewModel, cueListViewModel,
            relayViewModel, oscTriggerPanelManager, oscTriggerPanelViewModel, fileDialogService)
    {
        protected override bool ConfirmDiscardIfDirty() => true;
        protected override void NotifyLoadError(string filePath, Exception ex) { }
    }

    private MainViewModel CreateVm() => new TestMainViewModel(
        _projectService,
        _recentProjectsService,
        _cueManager,
        _hostRegistry,
        _timecodeRelay,
        _timecodeEngine,
        _timecodeViewModel,
        _cueListViewModel,
        _relayViewModel,
        _oscTriggerPanelManager,
        _oscTriggerPanelViewModel,
        new StubFileDialogServiceForMain());

    // --- Initial Title ---

    [Fact]
    public void Constructor_InitialTitleIsTimecodeBridge()
    {
        var vm = CreateVm();

        Assert.Equal("TimecodeBridge", vm.Title);
    }

    // --- SaveProjectAs ---

    [Fact]
    public void SaveProjectAsCommand_SavesProjectToSpecifiedPath()
    {
        var vm = CreateVm();

        vm.SaveProjectAsCommand.Execute("C:/test/project.json");

        Assert.Equal("C:/test/project.json", _projectService.LastSavedPath);
        Assert.NotNull(_projectService.LastSavedData);
    }

    [Fact]
    public void SaveProjectAsCommand_BuildsProjectDataFromCurrentState()
    {
        // Arrange: set up services with some state
        _cueManager.AddCue(new Cue
        {
            Id = "cue1",
            Name = "Test Cue",
            OscAddress = "/test",
            TriggerTime = new TimecodeValue(0, 0, 1, 0, FrameRate.Fps30),
        });
        _hostRegistry.AddHost(new OscHost
        {
            Id = "host1",
            Name = "Host A",
            IpAddress = "192.168.1.1",
            Port = 8000,
        });
        _timecodeRelay.OscAddressPattern = "/custom/tc";
        _timecodeRelay.IsContinuousEnabled = true;
        _timecodeRelay.ContinuousInterval = new RelayInterval(RelayIntervalMode.Custom, 100);
        _timecodeRelay.TargetHostIds = new List<string> { "host1" };
        _timecodeEngine.Offset = new TimecodeOffset(false, 1, 0, 0, 0, FrameRate.Fps30);

        var vm = CreateVm();

        vm.SaveProjectAsCommand.Execute("C:/test/project.json");

        var data = _projectService.LastSavedData!;
        Assert.Single(data.Cues);
        Assert.Equal("cue1", data.Cues[0].Id);
        Assert.Single(data.Hosts);
        Assert.Equal("host1", data.Hosts[0].Id);
        Assert.Equal("/custom/tc", data.RelaySettings.OscAddressPattern);
        Assert.True(data.RelaySettings.IsContinuousEnabled);
        Assert.Equal(100, data.RelaySettings.ContinuousInterval.IntervalMs);
        Assert.Single(data.RelaySettings.TargetHostIds);
        Assert.Equal(1, data.Offset.Hours);
    }

    [Fact]
    public void SaveProjectAsCommand_UpdatesTitleWithFileName()
    {
        var vm = CreateVm();

        vm.SaveProjectAsCommand.Execute("C:/test/myproject.json");

        Assert.Equal("TimecodeBridge - myproject.json", vm.Title);
    }

    // --- OpenProject ---

    [Fact]
    public void OpenProjectCommand_LoadsProjectAndRestoresServiceState()
    {
        var projectData = new ProjectData
        {
            Cues =
            [
                new Cue
                {
                    Id = "c1",
                    Name = "Loaded Cue",
                    OscAddress = "/loaded",
                    TriggerTime = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30),
                },
            ],
            Hosts =
            [
                new OscHost
                {
                    Id = "h1",
                    Name = "Loaded Host",
                    IpAddress = "10.0.0.1",
                    Port = 7000,
                },
            ],
            RelaySettings = new RelaySettings
            {
                OscAddressPattern = "/loaded/tc",
                IsContinuousEnabled = true,
                ContinuousInterval = new RelayInterval(RelayIntervalMode.Custom, 50),
                TargetHostIds = ["h1"],
            },
            Offset = new TimecodeOffset(true, 0, 1, 0, 0, FrameRate.Fps30),
        };
        _projectService.ProjectDataToLoad = projectData;

        var vm = CreateVm();

        vm.OpenProjectCommand.Execute("C:/test/loaded.json");

        // Verify cues restored
        Assert.Single(_cueManager.Cues);
        Assert.Equal("c1", _cueManager.Cues[0].Id);

        // Verify hosts restored
        Assert.Single(_hostRegistry.Hosts);
        Assert.Equal("h1", _hostRegistry.Hosts[0].Id);

        // Verify relay settings restored
        Assert.Equal("/loaded/tc", _timecodeRelay.OscAddressPattern);
        Assert.True(_timecodeRelay.IsContinuousEnabled);
        Assert.Equal(50, _timecodeRelay.ContinuousInterval.IntervalMs);
        Assert.Single(_timecodeRelay.TargetHostIds);

        // Verify offset restored
        Assert.True(_timecodeEngine.Offset.IsNegative);
        Assert.Equal(1, _timecodeEngine.Offset.Minutes);
    }

    [Fact]
    public void OpenProjectCommand_ClearsExistingStateBeforeLoading()
    {
        // Pre-populate services
        _cueManager.AddCue(new Cue
        {
            Id = "old-cue",
            Name = "Old",
            OscAddress = "/old",
            TriggerTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30),
        });
        _hostRegistry.AddHost(new OscHost
        {
            Id = "old-host",
            Name = "Old Host",
            IpAddress = "1.1.1.1",
            Port = 1000,
        });

        var projectData = new ProjectData
        {
            Cues =
            [
                new Cue
                {
                    Id = "new-cue",
                    Name = "New",
                    OscAddress = "/new",
                    TriggerTime = new TimecodeValue(0, 0, 1, 0, FrameRate.Fps30),
                },
            ],
            Hosts =
            [
                new OscHost
                {
                    Id = "new-host",
                    Name = "New Host",
                    IpAddress = "2.2.2.2",
                    Port = 2000,
                },
            ],
        };
        _projectService.ProjectDataToLoad = projectData;

        var vm = CreateVm();

        vm.OpenProjectCommand.Execute("C:/test/new.json");

        // Old state should be cleared, only new state present
        Assert.Single(_cueManager.Cues);
        Assert.Equal("new-cue", _cueManager.Cues[0].Id);
        Assert.Single(_hostRegistry.Hosts);
        Assert.Equal("new-host", _hostRegistry.Hosts[0].Id);
    }

    [Fact]
    public void OpenProjectCommand_UpdatesTitleWithFileName()
    {
        _projectService.ProjectDataToLoad = new ProjectData();
        var vm = CreateVm();

        vm.OpenProjectCommand.Execute("C:/projects/show.json");

        Assert.Equal("TimecodeBridge - show.json", vm.Title);
    }

    [Fact]
    public void OpenProjectCommand_AddsToRecentProjects()
    {
        _projectService.ProjectDataToLoad = new ProjectData();
        var vm = CreateVm();

        vm.OpenProjectCommand.Execute("C:/projects/show.json");

        Assert.Single(vm.RecentProjects);
        Assert.Equal("C:/projects/show.json", vm.RecentProjects[0]);
    }

    // --- HasUnsavedChanges and Title with "*" ---

    [Fact]
    public void HasUnsavedChanges_SyncsWithProjectService()
    {
        var vm = CreateVm();

        Assert.False(vm.HasUnsavedChanges);

        _projectService.SimulateUnsavedChanges(true);

        Assert.True(vm.HasUnsavedChanges);
    }

    [Fact]
    public void Title_ShowsAsteriskWhenUnsavedChanges()
    {
        var vm = CreateVm();
        vm.SaveProjectAsCommand.Execute("C:/test/project.json");

        _projectService.SimulateUnsavedChanges(true);

        Assert.Equal("TimecodeBridge - project.json *", vm.Title);
    }

    [Fact]
    public void Title_RemovesAsteriskWhenSaved()
    {
        var vm = CreateVm();

        _projectService.SimulateUnsavedChanges(true);

        Assert.Equal("TimecodeBridge *", vm.Title);

        _projectService.SimulateUnsavedChanges(false);

        Assert.Equal("TimecodeBridge", vm.Title);
    }

    // --- RecentProjects ---

    [Fact]
    public void RecentProjects_ReturnsListFromRecentProjectsService()
    {
        var vm = CreateVm();

        // Initially empty
        Assert.Empty(vm.RecentProjects);

        // After save, recent projects should update
        vm.SaveProjectAsCommand.Execute("C:/test/a.json");

        Assert.Single(vm.RecentProjects);
        Assert.Equal("C:/test/a.json", vm.RecentProjects[0]);
    }

    // --- NewProject ---

    [Fact]
    public void NewProjectCommand_ClearsCuesAndHosts()
    {
        _cueManager.AddCue(new Cue
        {
            Id = "c1",
            Name = "Test",
            OscAddress = "/test",
            TriggerTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30),
        });
        _hostRegistry.AddHost(new OscHost
        {
            Id = "h1",
            Name = "Test Host",
            IpAddress = "1.1.1.1",
            Port = 1000,
        });

        var vm = CreateVm();

        vm.NewProjectCommand.Execute(null);

        Assert.Empty(_cueManager.Cues);
        Assert.Empty(_hostRegistry.Hosts);
    }

    [Fact]
    public void NewProjectCommand_ResetsTitleToDefault()
    {
        var vm = CreateVm();
        vm.SaveProjectAsCommand.Execute("C:/test/project.json");
        Assert.Contains("project.json", vm.Title);

        vm.NewProjectCommand.Execute(null);

        Assert.Equal("TimecodeBridge", vm.Title);
    }

    [Fact]
    public void NewProjectCommand_ResetsRelayAndEngineDefaults()
    {
        _timecodeRelay.OscAddressPattern = "/custom";
        _timecodeRelay.IsContinuousEnabled = true;
        _timecodeEngine.Offset = new TimecodeOffset(true, 1, 0, 0, 0, FrameRate.Fps30);

        var vm = CreateVm();

        vm.NewProjectCommand.Execute(null);

        Assert.Equal("/timecode", _timecodeRelay.OscAddressPattern);
        Assert.False(_timecodeRelay.IsContinuousEnabled);
        Assert.Equal(TimecodeOffset.Zero(FrameRate.Fps30), _timecodeEngine.Offset);
    }

    // --- SaveProject (uses CurrentFilePath or parameter) ---

    [Fact]
    public void SaveProjectCommand_WhenCurrentFilePathExists_SavesThere()
    {
        var vm = CreateVm();
        vm.SaveProjectAsCommand.Execute("C:/test/existing.json");

        // Now SaveProject should save to the same path
        vm.SaveProjectCommand.Execute("C:/test/existing.json");

        Assert.Equal("C:/test/existing.json", _projectService.LastSavedPath);
    }

    // --- Dispose (event unsubscription) ---

    [Fact]
    public void Dispose_UnsubscribesFromUnsavedChangesStatusChanged()
    {
        var vm = CreateVm();

        Assert.False(vm.HasUnsavedChanges);

        vm.Dispose();

        // After dispose, project service changes should NOT update ViewModel
        _projectService.SimulateUnsavedChanges(true);

        Assert.False(vm.HasUnsavedChanges);
    }

    // --- SaveProject includes SourceSettings ---

    [Fact]
    public void SaveProjectAsCommand_IncludesSourceSettingsInProjectData()
    {
        var vm = CreateVm();

        vm.SaveProjectAsCommand.Execute("C:/test/project.json");

        var data = _projectService.LastSavedData!;
        Assert.NotNull(data.SourceSettings);
    }

    // --- Undo/Redo ---

    private Cue CreateCueForUndo(string id) => new()
    {
        Id = id,
        Name = $"Cue {id}",
        OscAddress = "/test",
        TriggerTime = new TimecodeValue(0, 0, 1, 0, FrameRate.Fps30),
    };

    [Fact]
    public void Undo_RestoresPreviousCueState()
    {
        var vm = CreateVm();
        Assert.False(vm.CanUndo);

        // 編集操作を模倣: キュー追加 + MarkAsChanged（VMの編集コマンドと同じ流れ）
        _cueManager.AddCue(CreateCueForUndo("c1"));
        _projectService.MarkAsChanged();

        Assert.True(vm.CanUndo);

        vm.UndoCommand.Execute(null);

        Assert.Empty(_cueManager.Cues);
        Assert.True(vm.CanRedo);
    }

    [Fact]
    public void Redo_ReappliesUndoneChange()
    {
        var vm = CreateVm();

        _cueManager.AddCue(CreateCueForUndo("c1"));
        _projectService.MarkAsChanged();

        vm.UndoCommand.Execute(null);
        Assert.Empty(_cueManager.Cues);

        vm.RedoCommand.Execute(null);

        Assert.Single(_cueManager.Cues);
        Assert.Equal("c1", _cueManager.Cues[0].Id);
        Assert.False(vm.CanRedo);
    }

    [Fact]
    public void NewProject_ResetsUndoHistory()
    {
        var vm = CreateVm();

        _cueManager.AddCue(CreateCueForUndo("c1"));
        _projectService.MarkAsChanged();
        Assert.True(vm.CanUndo);

        vm.NewProjectCommand.Execute(null);

        Assert.False(vm.CanUndo);
        Assert.False(vm.CanRedo);
    }

    [Fact]
    public void Undo_NewEditAfterUndo_DiscardsRedoHistory()
    {
        var vm = CreateVm();

        _cueManager.AddCue(CreateCueForUndo("c1"));
        _projectService.MarkAsChanged();
        System.Threading.Thread.Sleep(600); // 連続編集の集約ウィンドウを跨ぐ
        _cueManager.AddCue(CreateCueForUndo("c2"));
        _projectService.MarkAsChanged();

        vm.UndoCommand.Execute(null);
        Assert.Single(_cueManager.Cues);

        System.Threading.Thread.Sleep(600);
        _cueManager.AddCue(CreateCueForUndo("c3"));
        _projectService.MarkAsChanged();

        Assert.False(vm.CanRedo);
    }
}
