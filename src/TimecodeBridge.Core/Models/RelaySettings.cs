namespace TimecodeBridge.Core.Models;

public enum RelayIntervalMode
{
    EveryFrame,
    Custom,
}

public record struct RelayInterval(RelayIntervalMode Mode, int IntervalMs);

public class RelaySettings
{
    public string OscAddressPattern { get; set; } = "/timecode";
    public RelayInterval ContinuousInterval { get; set; } = new(RelayIntervalMode.EveryFrame, 0);
    public List<string> TargetHostIds { get; set; } = [];
    public bool IsContinuousEnabled { get; set; }
}
