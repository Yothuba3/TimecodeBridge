using TimecodeBridge.Models;
using TimecodeBridge.Services.Interfaces;

namespace TimecodeBridge.Services;

public class CueManager : ICueManager
{
    // _cues はUIスレッド（追加・編集）とタイムコードワーカー（列挙）の両方から触られる。
    // 変更は _gate で保護し、ワーカー側はスナップショットを列挙する。
    private readonly object _gate = new();
    private readonly List<Cue> _cues = [];
    private readonly ITimecodeEngine _timecodeEngine;
    private readonly IOscSender _oscSender;

    // _highWaterMark: the furthest TC we've seen in the current forward pass.
    // Cues fire only when TC advances past _highWaterMark into new territory.
    // Jitter never exceeds _highWaterMark, so it never causes re-triggers.
    private readonly HashSet<string> _firedCueIds = [];
    private TimecodeValue? _highWaterMark;
    private TimecodeValue? _lastRawTimecode;
    private volatile bool _trackingResetPending;

    // 編集されたキューの「発火済み」を解除するための引き継ぎキュー
    //（発火済み集合はワーカースレッド専有のため、UIスレッドからは直接触らない）
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _reArmQueue = new();

    public int TriggerWindowFrames { get; set; } = 3;
    public bool IsMuted { get; set; }

    public CueManager(ITimecodeEngine timecodeEngine, IOscSender oscSender)
    {
        _timecodeEngine = timecodeEngine;
        _oscSender = oscSender;
        _timecodeEngine.TimecodeUpdated += OnTimecodeUpdated;
    }

    public IReadOnlyList<Cue> Cues
    {
        get { lock (_gate) return _cues.ToList().AsReadOnly(); }
    }

    public void AddCue(Cue cue)
    {
        lock (_gate)
        {
            if (_cues.Any(c => c.Id == cue.Id))
            {
                throw new ArgumentException($"Cue with ID '{cue.Id}' already exists.", nameof(cue));
            }

            _cues.Add(cue);
        }
    }

    public void UpdateCue(string cueId, Cue updatedCue)
    {
        lock (_gate)
        {
            var index = _cues.FindIndex(c => c.Id == cueId);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Cue with ID '{cueId}' not found.");
            }

            updatedCue.Id = cueId;
            _cues[index] = updatedCue;
        }

        // 編集で発火時刻が未来へ移った場合に再発火できるよう、発火済みを解除する
        _reArmQueue.Enqueue(cueId);
    }

    public void RemoveCue(string cueId)
    {
        lock (_gate)
        {
            var index = _cues.FindIndex(c => c.Id == cueId);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Cue with ID '{cueId}' not found.");
            }

            _cues.RemoveAt(index);
        }
    }

    public void ReorderCues(IReadOnlyList<string> orderedCueIds)
    {
        lock (_gate)
        {
            var cueMap = _cues.ToDictionary(c => c.Id);
            var reordered = new List<Cue>(orderedCueIds.Count);

            foreach (var id in orderedCueIds)
            {
                if (cueMap.TryGetValue(id, out var cue))
                {
                    reordered.Add(cue);
                }
            }

            _cues.Clear();
            _cues.AddRange(reordered);
        }
    }

    public void SetCueEnabled(string cueId, bool enabled)
    {
        Cue? cue;
        lock (_gate)
        {
            cue = _cues.FirstOrDefault(c => c.Id == cueId);
        }
        if (cue is null)
        {
            throw new KeyNotFoundException($"Cue with ID '{cueId}' not found.");
        }

        cue.IsEnabled = enabled;
    }

    public void ManualTrigger(string cueId)
    {
        Cue? found;
        lock (_gate)
        {
            found = _cues.FirstOrDefault(c => c.Id == cueId);
        }
        var cue = found
            ?? throw new KeyNotFoundException($"Cue with ID '{cueId}' not found.");

        SendCueOsc(cue);
        CueTriggered?.Invoke(this, new CueTriggeredEventArgs
        {
            Cue = cue,
            TriggerTimecode = _timecodeEngine.CurrentOffsetTimecode,
            IsManual = true,
        });
    }

    // メインアドレス（引数あり）＋追加アドレス（引数なし）をまとめて送出する
    private void SendCueOsc(Cue cue)
    {
        var args = BuildArguments(cue);
        _oscSender.Send(cue.OscAddress, args, cue.TargetHostIds);

        foreach (var address in cue.AdditionalOscAddresses)
        {
            _oscSender.Send(address, [], cue.TargetHostIds);
        }
    }

    /// <summary>
    /// 再生位置の追跡状態（ハイウォーターマーク・発火済み集合）を仕切り直す。
    /// プロジェクト切替やジェネレーターリセットの前に呼ぶことで、位置ジャンプ時に
    /// 中間キューが一斉発火するのを防ぐ。実際のクリアはワーカースレッド側で行うためスレッド安全。
    /// </summary>
    public void ResetTracking() => _trackingResetPending = true;

    private void OnTimecodeUpdated(object? sender, TimecodeUpdatedEventArgs e)
    {
        // 追跡状態フィールドはワーカースレッドでのみ触る（ResetTrackingはフラグを立てるだけ）
        if (_trackingResetPending)
        {
            _trackingResetPending = false;
            _highWaterMark = null;
            _lastRawTimecode = null;
            _firedCueIds.Clear();
        }

        // 編集されたキューの発火済みを解除（UIスレッドからの引き継ぎ）
        while (_reArmQueue.TryDequeue(out var reArmId))
        {
            _firedCueIds.Remove(reArmId);
        }

        var tc = e.OffsetTimecode;
        long tcOrd = tc.ToOrdinal();

        // UIスレッドの変更と競合しないよう、この判定パスではスナップショットを列挙する
        Cue[] cues;
        lock (_gate) cues = _cues.ToArray();

        // When muted, keep tracking position but don't fire any cues.
        if (IsMuted)
        {
            _highWaterMark = tc;
            _lastRawTimecode = e.RawTimecode;
            return;
        }

        // Detect offset change: if raw TC barely moved but offset TC jumped,
        // the user changed the offset — reset high-water mark without triggering.
        if (_lastRawTimecode is not null && _highWaterMark is not null)
        {
            long rawDelta = Math.Abs(e.RawTimecode.ToOrdinal() - _lastRawTimecode.Value.ToOrdinal());
            long offsetDelta = Math.Abs(tcOrd - _highWaterMark.Value.ToOrdinal());
            if (rawDelta <= TriggerWindowFrames && offsetDelta > TriggerWindowFrames)
            {
                // Offset changed while raw TC stayed roughly the same
                _highWaterMark = tc;
                _lastRawTimecode = e.RawTimecode;
                return;
            }
        }
        _lastRawTimecode = e.RawTimecode;

        if (_highWaterMark is null)
        {
            _highWaterMark = tc;
            foreach (var cue in cues)
            {
                if (!cue.IsEnabled) continue;
                if (cue.GetEffectiveTriggerTime().ToOrdinal() == tcOrd)
                {
                    TriggerCue(cue, tc);
                    _firedCueIds.Add(cue.Id);
                }
            }
            return;
        }

        long hwmOrd = _highWaterMark.Value.ToOrdinal();

        // ── Rewind detection ──
        if (tcOrd < hwmOrd - TriggerWindowFrames)
        {
            _firedCueIds.RemoveWhere(id =>
            {
                var cue = cues.FirstOrDefault(c => c.Id == id);
                return cue is not null && cue.GetEffectiveTriggerTime().ToOrdinal() > tcOrd;
            });
            _highWaterMark = tc;
            return;
        }

        // ── Jitter / same frame / slight backward ──
        if (tcOrd <= hwmOrd)
        {
            return;
        }

        // ── 判定幅を超える前方ジャンプはシーク（頭出し）扱い ──
        // 途中のキューを一斉発火させず位置だけ移す。着地フレームちょうどのキューは
        // 受信開始時の完全一致ルールと同様に発火させる。
        // 通常再生の1フレーム前進は判定幅0でもジャンプ扱いにしない。
        if (tcOrd - hwmOrd > Math.Max(1, TriggerWindowFrames))
        {
            foreach (var cue in cues)
            {
                if (!cue.IsEnabled) continue;
                if (_firedCueIds.Contains(cue.Id)) continue;

                if (cue.GetEffectiveTriggerTime().ToOrdinal() == tcOrd)
                {
                    TriggerCue(cue, tc);
                    _firedCueIds.Add(cue.Id);
                }
            }

            _highWaterMark = tc;
            return;
        }

        // ── Forward into new territory ──
        foreach (var cue in cues)
        {
            if (!cue.IsEnabled) continue;
            if (_firedCueIds.Contains(cue.Id)) continue;

            long cueOrd = cue.GetEffectiveTriggerTime().ToOrdinal();
            if (cueOrd > hwmOrd && cueOrd <= tcOrd)
            {
                TriggerCue(cue, tc);
                _firedCueIds.Add(cue.Id);
            }
        }

        _highWaterMark = tc;
    }

    public void SendCueSync(string oscAddress, IReadOnlyList<string> targetHostIds)
    {
        var current = _timecodeEngine.CurrentOffsetTimecode;
        long currentOrd = current.ToOrdinal();

        // 有効かつ送信タイムコード指定ありのキューのうち、実効発火時刻が現在以前で最も近いもの
        Cue? baseCue = null;
        long bestOrd = long.MinValue;
        foreach (var cue in Cues)
        {
            if (!cue.IsEnabled || cue.SendTimecode is null) continue;

            long effectiveOrd = cue.GetEffectiveTriggerTime().ToOrdinal();
            if (effectiveOrd <= currentOrd && effectiveOrd > bestOrd)
            {
                baseCue = cue;
                bestOrd = effectiveOrd;
            }
        }

        float totalSeconds = 0f;
        if (baseCue is not null)
        {
            // 送信TC + (現在TC - 実効発火時刻) を秒ドメインで求める
            var sendTc = baseCue.SendTimecode!.Value;
            var effective = baseCue.GetEffectiveTriggerTime();

            // 同一フレームレートならフレーム演算で正確に、異なる場合はordinal(1秒=30)近似で経過秒を求める
            double elapsedSeconds = current.FrameRate == effective.FrameRate
                ? (current.TotalFrames() - effective.TotalFrames()) / (double)current.FrameRate.FramesPerSecond()
                : (currentOrd - bestOrd) / 30.0;

            double sendSeconds = sendTc.TotalFrames() / (double)sendTc.FrameRate.FramesPerSecond();
            totalSeconds = (float)(sendSeconds + elapsedSeconds);
        }

        _oscSender.Send(oscAddress, [new OscFloat32Argument(totalSeconds)], targetHostIds);
    }

    private void TriggerCue(Cue cue, TimecodeValue triggerTimecode)
    {
        SendCueOsc(cue);
        CueTriggered?.Invoke(this, new CueTriggeredEventArgs
        {
            Cue = cue,
            TriggerTimecode = triggerTimecode,
            IsManual = false,
        });
    }

    private static IReadOnlyList<OscArgument> BuildArguments(Cue cue)
    {
        if (!cue.SendTriggerTimeAsSeconds)
            return cue.Arguments;

        // 送信タイムコードが指定されていればそれを、なければトリガー時間を秒数化して送る
        var sendTime = cue.SendTimecode ?? cue.TriggerTime;
        float totalSeconds = sendTime.TotalFrames() / (float)sendTime.FrameRate.FramesPerSecond();

        var args = new List<OscArgument>(cue.Arguments.Count + 1);
        args.Add(new OscFloat32Argument(totalSeconds));
        args.AddRange(cue.Arguments);
        return args;
    }

    public event EventHandler<CueTriggeredEventArgs>? CueTriggered;
}
