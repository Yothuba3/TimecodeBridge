using System.Collections;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.Services.Interfaces;

namespace TimecodeBridge.macOS.ViewModels;

/// <summary>
/// CueリストViewModel（macOS版）
/// キュー管理、トリガー表示、次キュー算出を担当
/// </summary>
public partial class CueListViewModel : DispatcherViewModel
{
    private readonly ICueManager _cueManager;
    private readonly ITimecodeEngine _timecodeEngine;
    private readonly IHostRegistry _hostRegistry;
    private readonly ICueDialogService _cueDialogService;

    public ObservableCollection<CueItemViewModel> CueItems { get; } = [];

    public int TriggerWindowFrames
    {
        get => _cueManager.TriggerWindowFrames;
        set
        {
            if (_cueManager.TriggerWindowFrames != value)
            {
                _cueManager.TriggerWindowFrames = value;
                OnPropertyChanged();
            }
        }
    }

    public CueListViewModel(ICueManager cueManager, ITimecodeEngine timecodeEngine, IHostRegistry hostRegistry, ICueDialogService cueDialogService)
    {
        _cueManager = cueManager;
        _timecodeEngine = timecodeEngine;
        _hostRegistry = hostRegistry;
        _cueDialogService = cueDialogService;

        // Populate from existing cues
        foreach (var cue in _cueManager.Cues)
        {
            CueItems.Add(new CueItemViewModel(cue));
        }

        _cueManager.CueTriggered += OnCueTriggered;
        _timecodeEngine.TimecodeUpdated += OnTimecodeUpdated;
    }

    public void SyncFromService()
    {
        CueItems.Clear();
        foreach (var cue in _cueManager.Cues)
        {
            CueItems.Add(new CueItemViewModel(cue));
        }
    }

    [RelayCommand]
    private void AddCue()
    {
        var template = new Cue
        {
            Id = string.Empty,
            Name = $"Cue {_cueManager.Cues.Count + 1}",
            TriggerTime = new TimecodeValue(0, 0, 0, 0, _timecodeEngine.FrameRate),
            OscAddress = "/cue",
            TargetHostIds = _hostRegistry.Hosts.Select(h => h.Id).ToList(),
        };

        var result = _cueDialogService.ShowEditDialog(template, _hostRegistry.Hosts, _timecodeEngine.FrameRate, "キュー追加");
        if (result is not null)
        {
            result.Id = Guid.NewGuid().ToString();
            _cueManager.AddCue(result);
            CueItems.Add(new CueItemViewModel(result));
        }
    }

    [RelayCommand]
    private void EditCue(string? cueId)
    {
        if (cueId is null) return;
        var cue = _cueManager.Cues.FirstOrDefault(c => c.Id == cueId);
        if (cue is null) return;

        var result = _cueDialogService.ShowEditDialog(cue, _hostRegistry.Hosts, _timecodeEngine.FrameRate, "キュー編集");
        if (result is not null)
        {
            result.Id = cueId;
            _cueManager.UpdateCue(cueId, result);

            var index = -1;
            for (int i = 0; i < CueItems.Count; i++)
            {
                if (CueItems[i].Id == cueId) { index = i; break; }
            }
            if (index >= 0)
            {
                CueItems[index] = new CueItemViewModel(result);
            }
        }
    }

    [RelayCommand]
    private void BatchEditCues(IList? selectedItems)
    {
        if (selectedItems is null || selectedItems.Count < 2) return;

        var cueIds = selectedItems.OfType<CueItemViewModel>().Select(c => c.Id).ToList();
        if (cueIds.Count < 2) return;

        var result = _cueDialogService.ShowBatchEditDialog(cueIds.Count, _hostRegistry.Hosts, _timecodeEngine.FrameRate);
        if (result is null) return;

        foreach (var cueId in cueIds)
        {
            var cue = _cueManager.Cues.FirstOrDefault(c => c.Id == cueId);
            if (cue is null) continue;

            ApplyBatchEdit(cue, result);
            _cueManager.UpdateCue(cueId, cue);

            var index = -1;
            for (int i = 0; i < CueItems.Count; i++)
            {
                if (CueItems[i].Id == cueId) { index = i; break; }
            }
            if (index >= 0)
            {
                CueItems[index] = new CueItemViewModel(cue);
            }
        }
    }

    private static void ApplyBatchEdit(Cue cue, CueBatchEditResult edit)
    {
        if (edit.OscAddress is not null)
            cue.OscAddress = edit.OscAddress;
        if (edit.Arguments is not null)
            cue.Arguments = edit.Arguments.ToList();
        if (edit.TargetHostIds is not null)
            cue.TargetHostIds = edit.TargetHostIds.ToList();
        if (edit.IsEnabled.HasValue)
            cue.IsEnabled = edit.IsEnabled.Value;
        if (edit.SendTriggerTimeAsSeconds.HasValue)
            cue.SendTriggerTimeAsSeconds = edit.SendTriggerTimeAsSeconds.Value;
        if (edit.ApplyOffset)
            cue.CueOffset = edit.CueOffset;
        if (edit.ApplyMemo)
            cue.Memo = edit.Memo ?? string.Empty;
    }

    [RelayCommand]
    private void DuplicateCue(string? cueId)
    {
        if (cueId is null) return;
        var source = _cueManager.Cues.FirstOrDefault(c => c.Id == cueId);
        if (source is null) return;

        var duplicate = CloneCue(source, source.TriggerTime, source.Name + " (コピー)");
        AddCueInternal(duplicate);
    }

    [RelayCommand]
    private void BatchDuplicateCue(string? cueId)
    {
        if (cueId is null) return;
        var source = _cueManager.Cues.FirstOrDefault(c => c.Id == cueId);
        if (source is null) return;

        var batchResult = _cueDialogService.ShowBatchDuplicateDialog();
        if (batchResult is null) return;

        var (count, intervalHours) = batchResult.Value;
        int fps = source.TriggerTime.FrameRate.FramesPerSecond();
        long framesPerInterval = (long)intervalHours * 3600 * fps;
        long baseFrames = source.TriggerTime.TotalFrames();

        for (int i = 1; i <= count; i++)
        {
            long newTotalFrames = baseFrames + framesPerInterval * i;
            var newTriggerTime = TimecodeValue.FromTotalFrames(newTotalFrames, source.TriggerTime.FrameRate);

            var duplicate = CloneCue(source, newTriggerTime);
            AddCueInternal(duplicate);
        }
    }

    private static Cue CloneCue(Cue source, TimecodeValue triggerTime, string? nameOverride = null)
    {
        return new Cue
        {
            Id = Guid.NewGuid().ToString(),
            Name = nameOverride ?? source.Name,
            Memo = source.Memo,
            TriggerTime = triggerTime,
            OscAddress = source.OscAddress,
            Arguments = source.Arguments.ToList(),
            TargetHostIds = source.TargetHostIds.ToList(),
            IsEnabled = source.IsEnabled,
            SendTriggerTimeAsSeconds = source.SendTriggerTimeAsSeconds,
            CueOffset = source.CueOffset,
        };
    }

    private void AddCueInternal(Cue cue)
    {
        _cueManager.AddCue(cue);
        CueItems.Add(new CueItemViewModel(cue));
    }

    [RelayCommand]
    private void RemoveCue(string? cueId)
    {
        if (cueId is null) return;
        _cueManager.RemoveCue(cueId);
        var item = CueItems.FirstOrDefault(c => c.Id == cueId);
        if (item != null)
        {
            CueItems.Remove(item);
        }
    }

    [RelayCommand]
    private void ManualTrigger(string cueId)
    {
        _cueManager.ManualTrigger(cueId);
    }

    [RelayCommand]
    private void ToggleCueEnabled(string cueId)
    {
        var item = CueItems.FirstOrDefault(c => c.Id == cueId);
        if (item != null)
        {
            var newEnabled = !item.IsEnabled;
            _cueManager.SetCueEnabled(cueId, newEnabled);
            item.IsEnabled = newEnabled;
        }
    }

    private void OnCueTriggered(object? sender, CueTriggeredEventArgs e)
    {
        RunOnUIThread(() =>
        {
            var item = CueItems.FirstOrDefault(c => c.Id == e.Cue.Id);
            if (item == null) return;

            item.IsTriggered = true;

            // Use Task.Delay instead of DispatcherTimer for cross-platform compatibility
            Task.Delay(500).ContinueWith(_ =>
            {
                RunOnUIThread(() =>
                {
                    item.IsTriggered = false;
                });
            });
        });
    }

    private void OnTimecodeUpdated(object? sender, TimecodeUpdatedEventArgs e)
    {
        RunOnUIThread(() => UpdateNextCue(e.OffsetTimecode));
    }

    private void UpdateNextCue(TimecodeValue currentTimecode)
    {
        long currentOrd = currentTimecode.ToOrdinal();
        CueItemViewModel? nextCue = null;
        long nextOrd = long.MaxValue;

        foreach (var item in CueItems)
        {
            item.IsNextCue = false;

            if (!item.IsEnabled) continue;

            long cueOrd = item.TriggerTime.ToOrdinal();
            if (cueOrd > currentOrd && cueOrd < nextOrd)
            {
                nextCue = item;
                nextOrd = cueOrd;
            }
        }

        if (nextCue != null)
        {
            nextCue.IsNextCue = true;
        }
    }

    public override void Dispose()
    {
        _cueManager.CueTriggered -= OnCueTriggered;
        _timecodeEngine.TimecodeUpdated -= OnTimecodeUpdated;
    }
}
