using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.App.ViewModels;

public partial class RelayViewModel : ObservableObject, IDisposable
{
    private readonly ITimecodeRelay _timecodeRelay;
    private readonly IHostRegistry _hostRegistry;
    private readonly IProjectService _projectService;
    private bool _suppressDirty;

    public RelayViewModel(ITimecodeRelay timecodeRelay, IHostRegistry hostRegistry, IProjectService projectService)
    {
        _timecodeRelay = timecodeRelay;
        _hostRegistry = hostRegistry;
        _projectService = projectService;

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

    /// <summary>送信間隔モードのコンボ選択（0=EveryFrame, 1=Custom）。</summary>
    public int IntervalModeIndex
    {
        get => ContinuousInterval.Mode == RelayIntervalMode.Custom ? 1 : 0;
        set => ContinuousInterval = new RelayInterval(
            value == 1 ? RelayIntervalMode.Custom : RelayIntervalMode.EveryFrame,
            ContinuousInterval.IntervalMs);
    }

    /// <summary>Custom時の送信間隔ミリ秒。</summary>
    public int IntervalMs
    {
        get => ContinuousInterval.IntervalMs;
        set => ContinuousInterval = new RelayInterval(ContinuousInterval.Mode, Math.Max(0, value));
    }

    public void SyncFromService()
    {
        _suppressDirty = true;
        OscAddressPattern = _timecodeRelay.OscAddressPattern;
        IsContinuousEnabled = _timecodeRelay.IsContinuousEnabled;
        ContinuousInterval = _timecodeRelay.ContinuousInterval;
        TargetHostIds = _timecodeRelay.TargetHostIds;
        _suppressDirty = false;
        RefreshHostSelections();
    }

    private void MarkDirty()
    {
        if (!_suppressDirty) _projectService.MarkAsChanged();
    }

    partial void OnOscAddressPatternChanged(string value)
    {
        _timecodeRelay.OscAddressPattern = value;
        MarkDirty();
    }

    partial void OnIsContinuousEnabledChanged(bool value)
    {
        _timecodeRelay.IsContinuousEnabled = value;
        MarkDirty();
    }

    partial void OnContinuousIntervalChanged(RelayInterval value)
    {
        _timecodeRelay.ContinuousInterval = value;
        OnPropertyChanged(nameof(IntervalModeIndex));
        OnPropertyChanged(nameof(IntervalMs));
        MarkDirty();
    }

    partial void OnTargetHostIdsChanged(IReadOnlyList<string> value)
    {
        _timecodeRelay.TargetHostIds = value;
        MarkDirty();
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
        var ids = HostSelections.Where(h => h.IsSelected).Select(h => h.Id).ToList();

        // CheckBoxコンテナ生成時のイベント（プロジェクト読込直後など）で
        // 内容が変わっていないのにdirty化しないよう、実際の変化のみ反映する
        if (ids.SequenceEqual(TargetHostIds)) return;

        TargetHostIds = ids;
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
