using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.App.ViewModels;
using TimecodeBridge.Services.Interfaces;
using Xunit;

namespace TimecodeBridge.App.Tests.ViewModels;

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

    public void Reset()
    {
        CurrentFilePath = null;
        SetHasUnsavedChanges(false);
    }

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

internal class StubCueManagerForMain : ICueManager
{
    private readonly List<Cue> _cues = [];

    public IReadOnlyList<Cue> Cues => _cues.AsReadOnly();
    public int TriggerWindowFrames { get; set; } = 3;
    public bool IsMuted { get; set; }
        public bool IsAutoMuteEnabled { get; set; } = true;
        public event EventHandler? MuteStateChanged;

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

internal class StubTimecodeViewModel
{
    public TimecodeSourceSettings GetSourceSettings() => new TimecodeSourceSettings();
    public void RestoreSourceSettings(TimecodeSourceSettings settings) { }
}

internal class StubCueListViewModel
{
    public void SyncFromService() { }
}

internal class StubRelayViewModel
{
    public void SyncFromService() { }
}

internal class StubOscSenderForMain : IOscSender
{
    public event EventHandler<OscSendResultEventArgs>? SendCompleted;
    public void Send(string oscAddress, IReadOnlyList<OscArgument> arguments, IReadOnlyList<string> targetHostIds) { }
    public void SendPing(string hostId) { }
    public Task SendIcmpPingAsync(string hostId, int framesPerSecond) => Task.CompletedTask;
}

internal class StubHostDialogServiceForMain : IHostDialogService
{
    public OscHost? ShowEditDialog(OscHost template) => null;
}

internal class StubFileDialogService : IFileDialogService
{
    public string? FilePathToReturn { get; set; }

    public string? ShowOpenFileDialog(string filter, string? initialDirectory = null)
    {
        return FilePathToReturn;
    }

    public string? ShowSaveFileDialog(string filter, string? defaultFileName = null, string? initialDirectory = null)
    {
        return FilePathToReturn;
    }
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
    private readonly StubFileDialogService _fileDialogService = new();

    private class StubAudioDeviceServiceForMain : IAudioDeviceService
    {
        public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices() => [];
        public IReadOnlyList<AudioDeviceInfo> GetRenderDevices() => [];
    }

    private class StubCueDialogServiceForMain : ICueDialogService
    {
        public Cue? ShowEditDialog(Cue template, IReadOnlyList<OscHost> hosts, FrameRate frameRate, string title) => null;
        public CueBatchEditResult? ShowBatchEditDialog(int cueCount, IReadOnlyList<OscHost> hosts, FrameRate frameRate) => null;
        public (int Count, TimeSpan Interval)? ShowBatchDuplicateDialog() => null;
    }

    private class StubOscTriggerDialogServiceForMain : IOscTriggerDialogService
    {
        public OscTriggerEditResult ShowEditDialog(OscTriggerButton template, IReadOnlyList<OscHost> hosts, string title, bool canDelete)
            => new(OscTriggerEditAction.Cancel, null);
    }

    // ヘッドレス環境ではダイアログを出せないため、確認は常に許可・通知は無視する
    private class TestableMainViewModel : MainViewModel
    {
        public TestableMainViewModel(
            IProjectService projectService, TimecodeBridge.Core.Services.Interfaces.IFileDialogService fileDialogService,
            IRecentProjectsService recentProjectsService, ICueManager cueManager, IHostRegistry hostRegistry,
            ITimecodeRelay timecodeRelay, ITimecodeEngine timecodeEngine, IOscTriggerPanelManager oscTriggerPanelManager,
            TimecodeViewModel timecodeViewModel, CueListViewModel cueListViewModel, RelayViewModel relayViewModel,
            HostManagerViewModel hostManagerViewModel, OscTriggerPanelViewModel oscTriggerPanelViewModel, LogViewModel logViewModel)
            : base(projectService, fileDialogService, recentProjectsService, cueManager, hostRegistry, timecodeRelay,
                   timecodeEngine, oscTriggerPanelManager, timecodeViewModel, cueListViewModel, relayViewModel,
                   hostManagerViewModel, oscTriggerPanelViewModel, logViewModel)
        {
        }

        protected override bool ConfirmDiscardIfDirty() => true;
        protected override void NotifyLoadError(string filePath, Exception ex) { }
    }

    private MainViewModel CreateVm()
    {
        var oscSender = new StubOscSenderForMain();
        var cueSync = new CueSyncViewModel(_cueManager, _hostRegistry, _projectService);
        var timecodeViewModel = new TimecodeViewModel(_timecodeEngine, new StubAudioDeviceServiceForMain(), _cueManager, _projectService, cueSync);
        var cueListViewModel = new CueListViewModel(_cueManager, _timecodeEngine, _hostRegistry, new StubCueDialogServiceForMain(), _projectService);
        var relayViewModel = new RelayViewModel(_timecodeRelay, _hostRegistry, _projectService);
        var hostManagerViewModel = new HostManagerViewModel(_hostRegistry, oscSender, _timecodeEngine, new StubHostDialogServiceForMain(), _projectService);
        var panelManager = new TimecodeBridge.Core.Services.OscTriggerPanelManager(oscSender, _hostRegistry);
        var panelViewModel = new OscTriggerPanelViewModel(panelManager, new StubOscTriggerDialogServiceForMain(), _hostRegistry, _projectService);

        return new TestableMainViewModel(
            _projectService,
            _fileDialogService,
            _recentProjectsService,
            _cueManager,
            _hostRegistry,
            _timecodeRelay,
            _timecodeEngine,
            panelManager,
            timecodeViewModel,
            cueListViewModel,
            relayViewModel,
            hostManagerViewModel,
            panelViewModel,
            new LogViewModel(oscSender));
    }

    // --- Initial State ---

    [Fact]
    public void Constructor_InitialTitleIsTimecodeBridge()
    {
        var vm = CreateVm();

        Assert.Equal("TimecodeBridge", vm.Title);
    }

    [Fact]
    public void Constructor_HasUnsavedChangesIsFalse()
    {
        var vm = CreateVm();

        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public void Constructor_RecentProjectsIsEmpty()
    {
        var vm = CreateVm();

        Assert.Empty(vm.RecentProjects);
    }

    // --- NewProject Command ---

    [Fact]
    public async Task NewProjectCommand_ClearsCuesAndHosts()
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
    public async Task NewProjectCommand_ResetsTitleToDefault()
    {
        var vm = CreateVm();
        _fileDialogService.FilePathToReturn = "/test/project.json";
        vm.SaveProjectAsCommand.Execute(null);
        Assert.Contains("project.json", vm.Title);

        vm.NewProjectCommand.Execute(null);

        Assert.Equal("TimecodeBridge", vm.Title);
    }

    [Fact]
    public async Task NewProjectCommand_ResetsRelayAndEngineDefaults()
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

    // --- SaveProjectAs Command ---

    [Fact]
    public async Task SaveProjectAsCommand_ShowsSaveDialog()
    {
        var vm = CreateVm();
        _fileDialogService.FilePathToReturn = "/test/project.json";

        vm.SaveProjectAsCommand.Execute(null);

        Assert.Equal("/test/project.json", _projectService.LastSavedPath);
    }

    [Fact]
    public async Task SaveProjectAsCommand_WhenDialogCancelled_DoesNotSave()
    {
        var vm = CreateVm();
        _fileDialogService.FilePathToReturn = null;

        vm.SaveProjectAsCommand.Execute(null);

        Assert.Null(_projectService.LastSavedPath);
    }

    [Fact]
    public async Task SaveProjectAsCommand_BuildsProjectDataFromCurrentState()
    {
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
        _fileDialogService.FilePathToReturn = "/test/project.json";

        vm.SaveProjectAsCommand.Execute(null);

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
    public async Task SaveProjectAsCommand_UpdatesTitleWithFileName()
    {
        var vm = CreateVm();
        _fileDialogService.FilePathToReturn = "/test/myproject.json";

        vm.SaveProjectAsCommand.Execute(null);

        Assert.Equal("TimecodeBridge - myproject.json", vm.Title);
    }

    [Fact]
    public async Task SaveProjectAsCommand_AddsToRecentProjects()
    {
        var vm = CreateVm();
        _fileDialogService.FilePathToReturn = "/test/project.json";

        vm.SaveProjectAsCommand.Execute(null);

        Assert.Single(vm.RecentProjects);
        Assert.Equal("/test/project.json", vm.RecentProjects[0]);
    }

    [Fact]
    public async Task SaveProjectAsCommand_IncludesSourceSettingsInProjectData()
    {
        var vm = CreateVm();
        _fileDialogService.FilePathToReturn = "/test/project.json";

        vm.SaveProjectAsCommand.Execute(null);

        var data = _projectService.LastSavedData!;
        Assert.NotNull(data.SourceSettings);
    }

    // --- SaveProject Command ---

    [Fact]
    public async Task SaveProjectCommand_WhenCurrentFilePathExists_SavesThere()
    {
        var vm = CreateVm();
        _fileDialogService.FilePathToReturn = "/test/existing.json";
        vm.SaveProjectAsCommand.Execute(null);

        vm.SaveProjectCommand.Execute(null);

        Assert.Equal("/test/existing.json", _projectService.LastSavedPath);
    }

    [Fact]
    public async Task SaveProjectCommand_WhenNoCurrentFilePath_ShowsSaveAsDialog()
    {
        var vm = CreateVm();
        _fileDialogService.FilePathToReturn = "/test/new.json";

        vm.SaveProjectCommand.Execute(null);

        Assert.Equal("/test/new.json", _projectService.LastSavedPath);
    }

    // --- OpenProject Command ---

    [Fact]
    public async Task OpenProjectCommand_ShowsOpenDialog()
    {
        var projectData = new ProjectData();
        _projectService.ProjectDataToLoad = projectData;
        _fileDialogService.FilePathToReturn = "/test/loaded.json";

        var vm = CreateVm();

        vm.OpenProjectWithDialogCommand.Execute(null);

        Assert.Equal("/test/loaded.json", _projectService.CurrentFilePath);
    }

    [Fact]
    public async Task OpenProjectCommand_WhenDialogCancelled_DoesNotLoad()
    {
        var vm = CreateVm();
        _fileDialogService.FilePathToReturn = null;

        vm.OpenProjectWithDialogCommand.Execute(null);

        Assert.Null(_projectService.CurrentFilePath);
    }

    [Fact]
    public async Task OpenProjectCommand_LoadsProjectAndRestoresServiceState()
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
        _fileDialogService.FilePathToReturn = "/test/loaded.json";

        var vm = CreateVm();

        vm.OpenProjectWithDialogCommand.Execute(null);

        Assert.Single(_cueManager.Cues);
        Assert.Equal("c1", _cueManager.Cues[0].Id);
        Assert.Single(_hostRegistry.Hosts);
        Assert.Equal("h1", _hostRegistry.Hosts[0].Id);
        Assert.Equal("/loaded/tc", _timecodeRelay.OscAddressPattern);
        Assert.True(_timecodeRelay.IsContinuousEnabled);
        Assert.Equal(50, _timecodeRelay.ContinuousInterval.IntervalMs);
        Assert.Single(_timecodeRelay.TargetHostIds);
        Assert.True(_timecodeEngine.Offset.IsNegative);
        Assert.Equal(1, _timecodeEngine.Offset.Minutes);
    }

    [Fact]
    public async Task OpenProjectCommand_ClearsExistingStateBeforeLoading()
    {
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
        _fileDialogService.FilePathToReturn = "/test/new.json";

        var vm = CreateVm();

        vm.OpenProjectWithDialogCommand.Execute(null);

        Assert.Single(_cueManager.Cues);
        Assert.Equal("new-cue", _cueManager.Cues[0].Id);
        Assert.Single(_hostRegistry.Hosts);
        Assert.Equal("new-host", _hostRegistry.Hosts[0].Id);
    }

    [Fact]
    public async Task OpenProjectCommand_UpdatesTitleWithFileName()
    {
        _projectService.ProjectDataToLoad = new ProjectData();
        _fileDialogService.FilePathToReturn = "/projects/show.json";
        var vm = CreateVm();

        vm.OpenProjectWithDialogCommand.Execute(null);

        Assert.Equal("TimecodeBridge - show.json", vm.Title);
    }

    [Fact]
    public async Task OpenProjectCommand_AddsToRecentProjects()
    {
        _projectService.ProjectDataToLoad = new ProjectData();
        _fileDialogService.FilePathToReturn = "/projects/show.json";
        var vm = CreateVm();

        vm.OpenProjectWithDialogCommand.Execute(null);

        Assert.Single(vm.RecentProjects);
        Assert.Equal("/projects/show.json", vm.RecentProjects[0]);
    }

    // --- HasUnsavedChanges ---

    [Fact]
    public void HasUnsavedChanges_SyncsWithProjectService()
    {
        var vm = CreateVm();

        Assert.False(vm.HasUnsavedChanges);

        _projectService.SimulateUnsavedChanges(true);

        Assert.True(vm.HasUnsavedChanges);
    }

    [Fact]
    public async Task Title_ShowsAsteriskWhenUnsavedChanges()
    {
        var vm = CreateVm();
        _fileDialogService.FilePathToReturn = "/test/project.json";
        vm.SaveProjectAsCommand.Execute(null);

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
    public async Task RecentProjects_UpdatesAfterSave()
    {
        var vm = CreateVm();

        Assert.Empty(vm.RecentProjects);

        _fileDialogService.FilePathToReturn = "/test/a.json";
        vm.SaveProjectAsCommand.Execute(null);

        Assert.Single(vm.RecentProjects);
        Assert.Equal("/test/a.json", vm.RecentProjects[0]);
    }

    // --- Dispose ---

    [Fact]
    public void Dispose_UnsubscribesFromUnsavedChangesStatusChanged()
    {
        var vm = CreateVm();

        Assert.False(vm.HasUnsavedChanges);

        vm.Dispose();

        _projectService.SimulateUnsavedChanges(true);

        Assert.False(vm.HasUnsavedChanges);
    }
}
