using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Core.Services.Interfaces;

public interface ICueManager
{
    IReadOnlyList<Cue> Cues { get; }

    /// <summary>
    /// Trigger window size in frames. A cue fires when timecode enters
    /// [TriggerTime, TriggerTime + TriggerWindowFrames) and is suppressed
    /// until timecode leaves this window.
    /// </summary>
    int TriggerWindowFrames { get; set; }

    /// <summary>
    /// When true, timecode-triggered cues are suppressed. Manual triggers still work.
    /// </summary>
    bool IsMuted { get; set; }

    void AddCue(Cue cue);
    void UpdateCue(string cueId, Cue updatedCue);
    void RemoveCue(string cueId);
    void ReorderCues(IReadOnlyList<string> orderedCueIds);
    void SetCueEnabled(string cueId, bool enabled);
    void ManualTrigger(string cueId);
    event EventHandler<CueTriggeredEventArgs> CueTriggered;
}

public class CueTriggeredEventArgs : EventArgs
{
    public required Cue Cue { get; init; }
    public required TimecodeValue TriggerTimecode { get; init; }
    public required bool IsManual { get; init; }
}
