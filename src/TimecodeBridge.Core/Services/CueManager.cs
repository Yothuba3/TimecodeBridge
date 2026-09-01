using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Core.Services;

public class CueManager : ICueManager
{
    private readonly List<Cue> _cues = [];
    private readonly ITimecodeEngine _timecodeEngine;
    private readonly IOscSender _oscSender;

    // _highWaterMark: the furthest TC we've seen in the current forward pass.
    // Cues fire only when TC advances past _highWaterMark into new territory.
    // Jitter never exceeds _highWaterMark, so it never causes re-triggers.
    private readonly HashSet<string> _firedCueIds = [];
    private TimecodeValue? _highWaterMark;
    private TimecodeValue? _lastRawTimecode;

    public int TriggerWindowFrames { get; set; } = 3;
    public bool IsMuted { get; set; }

    public CueManager(ITimecodeEngine timecodeEngine, IOscSender oscSender)
    {
        _timecodeEngine = timecodeEngine;
        _oscSender = oscSender;
        _timecodeEngine.TimecodeUpdated += OnTimecodeUpdated;
    }

    public IReadOnlyList<Cue> Cues => _cues.AsReadOnly();

    public void AddCue(Cue cue)
    {
        if (_cues.Any(c => c.Id == cue.Id))
        {
            throw new ArgumentException($"Cue with ID '{cue.Id}' already exists.", nameof(cue));
        }

        _cues.Add(cue);
    }

    public void UpdateCue(string cueId, Cue updatedCue)
    {
        var index = _cues.FindIndex(c => c.Id == cueId);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Cue with ID '{cueId}' not found.");
        }

        updatedCue.Id = cueId;
        _cues[index] = updatedCue;
    }

    public void RemoveCue(string cueId)
    {
        var index = _cues.FindIndex(c => c.Id == cueId);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Cue with ID '{cueId}' not found.");
        }

        _cues.RemoveAt(index);
    }

    public void ReorderCues(IReadOnlyList<string> orderedCueIds)
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

    public void SetCueEnabled(string cueId, bool enabled)
    {
        var cue = _cues.FirstOrDefault(c => c.Id == cueId);
        if (cue is null)
        {
            throw new KeyNotFoundException($"Cue with ID '{cueId}' not found.");
        }

        cue.IsEnabled = enabled;
    }

    public void ManualTrigger(string cueId)
    {
        var cue = _cues.FirstOrDefault(c => c.Id == cueId)
            ?? throw new KeyNotFoundException($"Cue with ID '{cueId}' not found.");

        var args = BuildArguments(cue);
        _oscSender.Send(cue.OscAddress, args, cue.TargetHostIds);
        CueTriggered?.Invoke(this, new CueTriggeredEventArgs
        {
            Cue = cue,
            TriggerTimecode = _timecodeEngine.CurrentOffsetTimecode,
            IsManual = true,
        });
    }

    private void OnTimecodeUpdated(object? sender, TimecodeUpdatedEventArgs e)
    {
        var tc = e.OffsetTimecode;
        long tcOrd = tc.ToOrdinal();

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
            foreach (var cue in _cues)
            {
                if (!cue.IsEnabled) continue;
                if (cue.TriggerTime.ToOrdinal() == tcOrd)
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
                var cue = _cues.FirstOrDefault(c => c.Id == id);
                return cue is not null && cue.TriggerTime.ToOrdinal() > tcOrd;
            });
            _highWaterMark = tc;
            return;
        }

        // ── Jitter / same frame / slight backward ──
        if (tcOrd <= hwmOrd)
        {
            return;
        }

        // ── Forward into new territory ──
        foreach (var cue in _cues)
        {
            if (!cue.IsEnabled) continue;
            if (_firedCueIds.Contains(cue.Id)) continue;

            long cueOrd = cue.TriggerTime.ToOrdinal();
            if (cueOrd > hwmOrd && cueOrd <= tcOrd)
            {
                TriggerCue(cue, tc);
                _firedCueIds.Add(cue.Id);
            }
        }

        _highWaterMark = tc;
    }

    private void TriggerCue(Cue cue, TimecodeValue triggerTimecode)
    {
        var args = BuildArguments(cue);
        _oscSender.Send(cue.OscAddress, args, cue.TargetHostIds);
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

        var triggerTime = cue.TriggerTime;
        if (cue.CueOffset is { } offset)
            triggerTime = triggerTime.Add(offset);

        float totalSeconds = triggerTime.TotalFrames() / (float)triggerTime.FrameRate.FramesPerSecond();

        var args = new List<OscArgument>(cue.Arguments.Count + 1);
        args.Add(new OscFloat32Argument(totalSeconds));
        args.AddRange(cue.Arguments);
        return args;
    }

    public event EventHandler<CueTriggeredEventArgs>? CueTriggered;
}
