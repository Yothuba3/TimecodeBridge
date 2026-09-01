using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.Services.Interfaces;

namespace TimecodeBridge.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IProjectService _projectService;
    private readonly IRecentProjectsService _recentProjectsService;
    private readonly ICueManager _cueManager;
    private readonly IHostRegistry _hostRegistry;
    private readonly ITimecodeRelay _timecodeRelay;
    private readonly ITimecodeEngine _timecodeEngine;
    private readonly TimecodeViewModel _timecodeViewModel;
    private readonly CueListViewModel _cueListViewModel;
    private readonly RelayViewModel _relayViewModel;
    private bool _isNewProject = true;

    [ObservableProperty]
    private string _title = "TimecodeBridge";

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private IReadOnlyList<string> _recentProjects = [];

    [ObservableProperty]
    private string _statusMessage = "Ready";

    public MainViewModel(
        IProjectService projectService,
        IRecentProjectsService recentProjectsService,
        ICueManager cueManager,
        IHostRegistry hostRegistry,
        ITimecodeRelay timecodeRelay,
        ITimecodeEngine timecodeEngine,
        TimecodeViewModel timecodeViewModel,
        CueListViewModel cueListViewModel,
        RelayViewModel relayViewModel)
    {
        _projectService = projectService;
        _recentProjectsService = recentProjectsService;
        _cueManager = cueManager;
        _hostRegistry = hostRegistry;
        _timecodeRelay = timecodeRelay;
        _timecodeEngine = timecodeEngine;
        _timecodeViewModel = timecodeViewModel;
        _cueListViewModel = cueListViewModel;
        _relayViewModel = relayViewModel;

        RecentProjects = _recentProjectsService.GetRecentProjects();
        _projectService.UnsavedChangesStatusChanged += OnUnsavedChangesStatusChanged;

    }

    [RelayCommand]
    private void NewProject()
    {
        ClearAllData();

        // Reset relay settings
        _timecodeRelay.OscAddressPattern = "/timecode";
        _timecodeRelay.ContinuousInterval = new RelayInterval(RelayIntervalMode.EveryFrame, 0);
        _timecodeRelay.TargetHostIds = [];
        _timecodeRelay.IsContinuousEnabled = false;

        // Reset engine offset
        _timecodeEngine.Offset = TimecodeOffset.Zero(_timecodeEngine.FrameRate);

        // Reset source settings
        _timecodeViewModel.RestoreSourceSettings(new TimecodeSourceSettings());

        // Sync child ViewModels
        _cueListViewModel.SyncFromService();
        _relayViewModel.SyncFromService();

        _isNewProject = true;
        UpdateTitle();
    }

    [RelayCommand]
    private void SaveProject(string? filePath)
    {
        var path = filePath ?? _projectService.CurrentFilePath;
        if (path is null) return;

        SaveToPath(path);
    }

    [RelayCommand]
    private void SaveProjectAs(string filePath)
    {
        SaveToPath(filePath);
    }

    [RelayCommand]
    private void OpenProject(string filePath)
    {
        _isNewProject = false;
        var data = _projectService.LoadProject(filePath);

        // MRUリストに追加（ProjectServiceの責務外）
        _recentProjectsService.AddRecentProject(filePath);

        ClearAllData();

        // Restore cues
        foreach (var cue in data.Cues)
        {
            _cueManager.AddCue(cue);
        }

        // Restore hosts
        foreach (var host in data.Hosts)
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

        // Restore source settings
        _timecodeViewModel.RestoreSourceSettings(data.SourceSettings);

        // Sync child ViewModels
        _cueListViewModel.SyncFromService();
        _relayViewModel.SyncFromService();

        UpdateTitle();
        RecentProjects = _recentProjectsService.GetRecentProjects();
    }

    private void ClearAllData()
    {
        foreach (var cue in _cueManager.Cues.ToList())
        {
            _cueManager.RemoveCue(cue.Id);
        }

        foreach (var host in _hostRegistry.Hosts.ToList())
        {
            _hostRegistry.RemoveHost(host.Id);
        }
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

        if (!_isNewProject)
        {
            var currentPath = _projectService.CurrentFilePath;
            if (currentPath is not null)
            {
                var fileName = System.IO.Path.GetFileName(currentPath);
                title = $"TimecodeBridge - {fileName}";
            }
        }

        if (_projectService.HasUnsavedChanges)
        {
            title += " *";
        }

        Title = title;
    }

    public void Dispose()
    {
        _projectService.UnsavedChangesStatusChanged -= OnUnsavedChangesStatusChanged;
    }
}
