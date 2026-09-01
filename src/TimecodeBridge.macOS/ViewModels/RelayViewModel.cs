using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.macOS.ViewModels;

public partial class RelayViewModel : ObservableObject, IDisposable
{
    private readonly ITimecodeRelay _timecodeRelay;
    private readonly IHostRegistry _hostRegistry;

    public RelayViewModel(ITimecodeRelay timecodeRelay, IHostRegistry hostRegistry)
    {
        _timecodeRelay = timecodeRelay;
        _hostRegistry = hostRegistry;

        _oscAddressPattern = _timecodeRelay.OscAddressPattern;
        _isContinuousEnabled = _timecodeRelay.IsContinuousEnabled;
        _continuousInterval = _timecodeRelay.ContinuousInterval;
        _targetHostIds = _timecodeRelay.TargetHostIds;

        RefreshHostSelections();
        _hostRegistry.HostChanged += OnHostChanged;
    }

    [ObservableProperty] private string _oscAddressPattern = "/timecode";
    [ObservableProperty] private bool _isContinuousEnabled;
    [ObservableProperty] private RelayInterval _continuousInterval;
    [ObservableProperty] private IReadOnlyList<string> _targetHostIds = [];

    public ObservableCollection<HostSelection> HostSelections { get; } = [];

    public void SyncFromService()
    {
        OscAddressPattern = _timecodeRelay.OscAddressPattern;
        IsContinuousEnabled = _timecodeRelay.IsContinuousEnabled;
        ContinuousInterval = _timecodeRelay.ContinuousInterval;
        TargetHostIds = _timecodeRelay.TargetHostIds;
        RefreshHostSelections();
    }

    partial void OnOscAddressPatternChanged(string value)
    {
        _timecodeRelay.OscAddressPattern = value;
    }

    partial void OnIsContinuousEnabledChanged(bool value)
    {
        _timecodeRelay.IsContinuousEnabled = value;
    }

    partial void OnContinuousIntervalChanged(RelayInterval value)
    {
        _timecodeRelay.ContinuousInterval = value;
    }

    partial void OnTargetHostIdsChanged(IReadOnlyList<string> value)
    {
        _timecodeRelay.TargetHostIds = value;
    }

    [RelayCommand]
    private void ToggleContinuous()
    {
        IsContinuousEnabled = !IsContinuousEnabled;
    }

    [RelayCommand]
    private void TriggerOneShot()
    {
        _timecodeRelay.TriggerOneShot();
    }

    [RelayCommand]
    private void UpdateHostSelections()
    {
        TargetHostIds = HostSelections.Where(h => h.IsSelected).Select(h => h.Id).ToList();
    }

    private void OnHostChanged(object? sender, HostChangedEventArgs e) => RefreshHostSelections();

    public void Dispose()
    {
        _hostRegistry.HostChanged -= OnHostChanged;
    }

    private void RefreshHostSelections()
    {
        var selectedIds = _timecodeRelay.TargetHostIds;
        HostSelections.Clear();
        foreach (var host in _hostRegistry.Hosts)
        {
            HostSelections.Add(new HostSelection
            {
                Id = host.Id,
                Name = host.Name,
                IsSelected = selectedIds.Contains(host.Id),
            });
        }
    }
}
