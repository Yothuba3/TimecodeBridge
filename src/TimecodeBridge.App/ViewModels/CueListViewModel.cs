using System.Collections;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.App.Services;

namespace TimecodeBridge.App.ViewModels;

public partial class CueListViewModel : DispatcherViewModel
{
    private readonly ICueManager _cueManager;
    private readonly ITimecodeEngine _timecodeEngine;
    private readonly IHostRegistry _hostRegistry;
    private readonly ICueDialogService _cueDialogService;
    private readonly IProjectService _projectService;

    public ObservableCollection<CueItemViewModel> CueItems { get; } = [];

    public int TriggerWindowFrames
    {
        get => _cueManager.TriggerWindowFrames;
        set
        {
            // 負数は巻き戻し判定を壊すため0未満は受け付けない（プロジェクト非永続の実行時設定）
            var clamped = Math.Max(0, value);
            if (_cueManager.TriggerWindowFrames != clamped)
            {
                _cueManager.TriggerWindowFrames = clamped;
            }
            // 補正値の表示反映はDispatcher経由で行う
            Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(TriggerWindowFrames)));
        }
    }

    public CueListViewModel(ICueManager cueManager, ITimecodeEngine timecodeEngine, IHostRegistry hostRegistry, ICueDialogService cueDialogService, IProjectService projectService)
    {
        _cueManager = cueManager;
        _timecodeEngine = timecodeEngine;
        _hostRegistry = hostRegistry;
        _cueDialogService = cueDialogService;
        _projectService = projectService;

        // Populate from existing cues
        foreach (var cue in _cueManager.Cues)
        {
            CueItems.Add(new CueItemViewModel(cue));
        }

        _cueManager.CueTriggered += OnCueTriggered;
        _timecodeEngine.TimecodeUpdated += OnTimecodeUpdated;
        _cueManager.MuteStateChanged += OnMuteStateChanged;
    }

    // --- オートミュートのカウントダウン表示 ---

    private DispatcherTimer? _muteCountdownTimer;

    private void OnMuteStateChanged(object? sender, EventArgs e)
    {
        RunOnUIThread(RefreshMuteCountdown);
    }

    /// <summary>オートミュート状況を各キュー行の表示へ反映する（UIスレッドで呼ぶこと）。</summary>
    internal void RefreshMuteCountdown()
    {
        var mutedCueId = _cueManager.AutoMutedCueId;
        var unmuteAt = _cueManager.AutoUnmuteAt;

        foreach (var item in CueItems)
        {
            if (item.Id != mutedCueId)
            {
                if (item.MuteCountdownText.Length > 0) item.MuteCountdownText = "";
                continue;
            }

            if (unmuteAt is { } at)
            {
                var remaining = at - DateTime.UtcNow;
                if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
                item.MuteCountdownText = $"解除まで {FormatRemaining(remaining, item.TriggerTime.FrameRate.FramesPerSecond())}";
            }
            else
            {
                item.MuteCountdownText = "MUTE中";
            }
        }

        // 時限解除中のみタイマーで表示を進める
        bool ticking = mutedCueId is not null && unmuteAt is not null;
        if (ticking)
        {
            _muteCountdownTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _muteCountdownTimer.Tick -= OnMuteCountdownTick;
            _muteCountdownTimer.Tick += OnMuteCountdownTick;
            _muteCountdownTimer.Start();
        }
        else
        {
            _muteCountdownTimer?.Stop();
        }
    }

    private void OnMuteCountdownTick(object? sender, EventArgs e) => RefreshMuteCountdown();

    private static string FormatRemaining(TimeSpan remaining, int fps)
    {
        int frames = (int)(remaining.Milliseconds / 1000.0 * fps);
        return $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}:{frames:D2}";
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
            AddCueInternal(result);
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

            // 項目を差し替えず更新して選択状態を維持する
            CueItems.FirstOrDefault(c => c.Id == cueId)?.Update(result);
            RefreshNextCue();
            _projectService.MarkAsChanged();
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

        int offsetSkipped = 0;
        foreach (var cueId in cueIds)
        {
            var cue = _cueManager.Cues.FirstOrDefault(c => c.Id == cueId);
            if (cue is null) continue;

            // トリガーオフセット適用で発火時刻が0〜24時の範囲外になるキューには適用しない
            if (result.ApplyTriggerOffset &&
                !Cue.TryApplyTriggerOffset(cue.TriggerTime, result.TriggerOffset, out _))
            {
                offsetSkipped++;
                var withoutOffset = new CueBatchEditResult
                {
                    OscAddress = result.OscAddress,
                    AdditionalOscAddresses = result.AdditionalOscAddresses,
                    Arguments = result.Arguments,
                    TargetHostIds = result.TargetHostIds,
                    IsEnabled = result.IsEnabled,
                    SendTriggerTimeAsSeconds = result.SendTriggerTimeAsSeconds,
                    ApplySendTimecode = result.ApplySendTimecode,
                    SendTimecode = result.SendTimecode,
                    ApplyMemo = result.ApplyMemo,
                    Memo = result.Memo,
                };
                ApplyBatchEdit(cue, withoutOffset);
            }
            else
            {
                ApplyBatchEdit(cue, result);
            }

            _cueManager.UpdateCue(cueId, cue);

            // 項目を差し替えず更新して選択状態を維持する
            CueItems.FirstOrDefault(c => c.Id == cueId)?.Update(cue);
        }

        if (offsetSkipped > 0)
        {
            NotifyTriggerOffsetSkipped(offsetSkipped);
        }

        RefreshNextCue();
        _projectService.MarkAsChanged();
    }

    /// <summary>範囲外のためトリガーオフセットを適用しなかったキューの通知。テスト時に差し替え可能。</summary>
    protected virtual void NotifyTriggerOffsetSkipped(int count)
    {
        ModalDialog.ShowMessage("一括編集",
            $"{count} 件のキューは、トリガーオフセット適用後の発火時刻が 0〜24時 の範囲を超えるため、オフセットを適用しませんでした（他の項目は適用済み）。");
    }

    private static void ApplyBatchEdit(Cue cue, CueBatchEditResult edit)
    {
        if (edit.OscAddress is not null)
            cue.OscAddress = edit.OscAddress;
        if (edit.AdditionalOscAddresses is not null)
            cue.AdditionalOscAddresses = edit.AdditionalOscAddresses.ToList();
        if (edit.Arguments is not null)
            cue.Arguments = edit.Arguments.ToList();
        if (edit.TargetHostIds is not null)
            cue.TargetHostIds = edit.TargetHostIds.ToList();
        if (edit.IsEnabled.HasValue)
            cue.IsEnabled = edit.IsEnabled.Value;
        if (edit.SendTriggerTimeAsSeconds.HasValue)
            cue.SendTriggerTimeAsSeconds = edit.SendTriggerTimeAsSeconds.Value;
        if (edit.ApplySendTimecode)
            cue.SendTimecode = edit.SendTimecode;
        if (edit.ApplyTriggerOffset)
            cue.TriggerOffset = edit.TriggerOffset;
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

        var (count, interval) = batchResult.Value;
        int fps = source.TriggerTime.FrameRate.FramesPerSecond();
        long framesPerInterval = (long)interval.TotalSeconds * fps;
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
            AdditionalOscAddresses = source.AdditionalOscAddresses.ToList(),
            Arguments = source.Arguments.ToList(),
            TargetHostIds = source.TargetHostIds.ToList(),
            IsEnabled = source.IsEnabled,
            SendTriggerTimeAsSeconds = source.SendTriggerTimeAsSeconds,
            SendTimecode = source.SendTimecode,
            TriggerOffset = source.TriggerOffset,
            AutoMuteOnFire = source.AutoMuteOnFire,
            AutoUnmuteAfter = source.AutoUnmuteAfter,
        };
    }

    private void AddCueInternal(Cue cue)
    {
        _cueManager.AddCue(cue);
        CueItems.Add(new CueItemViewModel(cue));
        _projectService.MarkAsChanged();
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
        _projectService.MarkAsChanged();
    }

    [RelayCommand]
    private void RemoveCues(IList? selectedItems)
    {
        if (selectedItems is null || selectedItems.Count == 0) return;

        // SelectedItemsは削除中に変化するためコピーしてから処理する
        var items = selectedItems.OfType<CueItemViewModel>().ToList();
        if (items.Count == 0) return;

        if (!ConfirmRemoveCues(items)) return;

        foreach (var item in items)
        {
            _cueManager.RemoveCue(item.Id);
            CueItems.Remove(item);
        }
        _projectService.MarkAsChanged();
    }

    /// <summary>キュー削除の確認。テスト時に差し替え可能。</summary>
    protected virtual bool ConfirmRemoveCues(IReadOnlyList<CueItemViewModel> items)
    {
        var names = string.Join("\n", items.Take(5).Select(i => $"・{i.TriggerTime}  {i.Name}"));
        if (items.Count > 5) names += $"\n… ほか {items.Count - 5} 件";

        return ModalDialog.Confirm("キュー削除の確認",
            $"{items.Count} 件のキューを削除しますか？\n\n{names}", "削除");
    }

    [RelayCommand]
    private void SortCuesByTime()
    {
        // 表示上のトリガー時間ではなく、オフセット適用後の実際の発火順に並べる
        var ordered = CueItems.OrderBy(c => c.GetEffectiveTriggerTime().ToOrdinal()).ToList();
        if (ordered.SequenceEqual(CueItems)) return;

        _cueManager.ReorderCues(ordered.Select(c => c.Id).ToList());
        CueItems.Clear();
        foreach (var item in ordered)
        {
            CueItems.Add(item);
        }
        _projectService.MarkAsChanged();
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
            // 一覧のCheckBoxはOneWay表示なので、ここで反転してManagerと表示へ反映する
            var newValue = !item.IsEnabled;
            item.IsEnabled = newValue;
            _cueManager.SetCueEnabled(cueId, newValue);
            _projectService.MarkAsChanged();
        }
    }

    /// <summary>次キューが変わったとき（画面外なら表示位置を追従させる用途）。UIスレッドで発火。</summary>
    public event EventHandler<CueItemViewModel>? NextCueChanged;

    private TimecodeValue? _lastTimecode;
    private CueItemViewModel? _currentNextCue;

    /// <summary>ヘッダ常時表示用の次キュー要約。</summary>
    [ObservableProperty] private string _nextCueSummary = "NEXT: なし";

    private void OnCueTriggered(object? sender, CueTriggeredEventArgs e)
    {
        RunOnUIThread(() =>
        {
            CueItems.FirstOrDefault(c => c.Id == e.Cue.Id)?.FlashTriggered();
        });
    }

    private void OnTimecodeUpdated(object? sender, TimecodeUpdatedEventArgs e)
    {
        RunOnUIThread(() =>
        {
            _lastTimecode = e.OffsetTimecode;
            UpdateNextCue(e.OffsetTimecode);
        });
    }

    /// <summary>編集後などに次キューハイライトを再計算する（タイムコード未受信なら何もしない）。</summary>
    private void RefreshNextCue()
    {
        if (_lastTimecode is { } tc) UpdateNextCue(tc);
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

            // トリガーオフセット適用後の実際の発火時刻で判定する
            long cueOrd = item.GetEffectiveTriggerTime().ToOrdinal();
            if (cueOrd > currentOrd && cueOrd < nextOrd)
            {
                nextCue = item;
                nextOrd = cueOrd;
            }
        }

        if (nextCue != null)
        {
            nextCue.IsNextCue = true;

            // ordinalは30固定基準（1秒=30）なので、実FPSのフレーム数として復元せず秒に換算する
            var remaining = TimeSpan.FromSeconds((nextOrd - currentOrd) / 30.0);
            NextCueSummary = $"NEXT {nextCue.GetEffectiveTriggerTime()} {nextCue.Name}（あと {remaining:hh\\:mm\\:ss}）";
        }
        else
        {
            NextCueSummary = "NEXT: なし";
        }

        if (!ReferenceEquals(nextCue, _currentNextCue))
        {
            _currentNextCue = nextCue;
            if (nextCue is not null)
            {
                NextCueChanged?.Invoke(this, nextCue);
            }
        }
    }

    public override void Dispose()
    {
        _muteCountdownTimer?.Stop();
        _cueManager.CueTriggered -= OnCueTriggered;
        _timecodeEngine.TimecodeUpdated -= OnTimecodeUpdated;
        _cueManager.MuteStateChanged -= OnMuteStateChanged;
        base.Dispose();
    }
}
