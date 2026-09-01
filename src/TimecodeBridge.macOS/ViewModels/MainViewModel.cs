using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.Services.Interfaces;

namespace TimecodeBridge.macOS.ViewModels;

/// <summary>
/// メインウィンドウのViewModel（macOS版）
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IProjectService _projectService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IRecentProjectsService _recentProjectsService;
    private readonly ICueManager _cueManager;
    private readonly IHostRegistry _hostRegistry;
    private readonly ITimecodeRelay _timecodeRelay;
    private readonly ITimecodeEngine _timecodeEngine;
    private readonly object _timecodeViewModel;
    private readonly object _cueListViewModel;
    private readonly object _relayViewModel;
    private bool _isNewProject = true;

    [ObservableProperty]
    private string _title = "TimecodeBridge";

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private IReadOnlyList<string> _recentProjects = [];

    [ObservableProperty]
    private string _statusMessage = "Ready";

    // Child ViewModels
    public object TimecodeViewModel { get; }
    public object CueListViewModel { get; }
    public object RelayViewModel { get; }
    public HostManagerViewModel HostManagerViewModel { get; }
    public LogViewModel LogViewModel { get; }

    public MainViewModel(
        IProjectService projectService,
        IFileDialogService fileDialogService,
        IRecentProjectsService recentProjectsService,
        ICueManager cueManager,
        IHostRegistry hostRegistry,
        ITimecodeRelay timecodeRelay,
        ITimecodeEngine timecodeEngine,
        object timecodeViewModel,
        object cueListViewModel,
        object relayViewModel,
        HostManagerViewModel hostManagerViewModel,
        LogViewModel logViewModel)
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

        TimecodeViewModel = timecodeViewModel;
        CueListViewModel = cueListViewModel;
        RelayViewModel = relayViewModel;
        HostManagerViewModel = hostManagerViewModel;
        LogViewModel = logViewModel;

        RecentProjects = _recentProjectsService.GetRecentProjects();
        _projectService.UnsavedChangesStatusChanged += OnUnsavedChangesStatusChanged;

        // Initialize status message
        UpdateStatusMessage("アプリケーションを起動しました");
    }

    [RelayCommand]
    private async Task NewProject()
    {
        await Task.Run(() =>
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
            RestoreSourceSettings(new TimecodeSourceSettings());

            // Sync child ViewModels
            SyncCueListViewModel();
            SyncRelayViewModel();

            _isNewProject = true;
            UpdateTitle();
        });
    }

    [RelayCommand]
    private async Task SaveProject()
    {
        var path = _projectService.CurrentFilePath;
        if (path is null)
        {
            await SaveProjectAs();
            return;
        }

        await Task.Run(() => SaveToPath(path));
    }

    [RelayCommand]
    private async Task SaveProjectAs()
    {
        var filePath = _fileDialogService.ShowSaveFileDialog(
            "JSON files (*.json)|*.json|All files (*.*)|*.*",
            "project.json");

        if (filePath is null)
            return;

        await Task.Run(() => SaveToPath(filePath));
    }

    [RelayCommand]
    private async Task OpenProject()
    {
        var filePath = _fileDialogService.ShowOpenFileDialog(
            "JSON files (*.json)|*.json|All files (*.*)|*.*");

        if (filePath is null)
            return;

        await OpenRecentProject(filePath);
    }

    [RelayCommand]
    private async Task OpenRecentProject(string filePath)
    {
        await Task.Run(() =>
        {
            _isNewProject = false;
            var data = _projectService.LoadProject(filePath);

            // MRUリストに追加
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
            RestoreSourceSettings(data.SourceSettings);

            // Sync child ViewModels
            SyncCueListViewModel();
            SyncRelayViewModel();

            UpdateTitle();
            RecentProjects = _recentProjectsService.GetRecentProjects();
        });
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
            SourceSettings = GetSourceSettings(),
        };

        _isNewProject = false;
        _projectService.SaveProject(filePath, data);

        // MRUリストに追加
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

    private TimecodeSourceSettings GetSourceSettings()
    {
        // Use reflection to call GetSourceSettings on TimecodeViewModel
        var method = _timecodeViewModel.GetType().GetMethod("GetSourceSettings");
        return (TimecodeSourceSettings)(method?.Invoke(_timecodeViewModel, null) ?? new TimecodeSourceSettings());
    }

    private void RestoreSourceSettings(TimecodeSourceSettings settings)
    {
        // Use reflection to call RestoreSourceSettings on TimecodeViewModel
        var method = _timecodeViewModel.GetType().GetMethod("RestoreSourceSettings");
        method?.Invoke(_timecodeViewModel, new object[] { settings });
    }

    private void SyncCueListViewModel()
    {
        // Use reflection to call SyncFromService on CueListViewModel
        var method = _cueListViewModel.GetType().GetMethod("SyncFromService");
        method?.Invoke(_cueListViewModel, null);
    }

    private void SyncRelayViewModel()
    {
        // Use reflection to call SyncFromService on RelayViewModel
        var method = _relayViewModel.GetType().GetMethod("SyncFromService");
        method?.Invoke(_relayViewModel, null);
    }

    private void UpdateStatusMessage(string message)
    {
        StatusMessage = message;
    }

    public void Dispose()
    {
        _projectService.UnsavedChangesStatusChanged -= OnUnsavedChangesStatusChanged;
    }
}
