namespace TimecodeBridge.Core.Models;

public class Cue
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string Memo { get; set; } = string.Empty;
    public required TimecodeValue TriggerTime { get; set; }
    public required string OscAddress { get; set; }
    public List<OscArgument> Arguments { get; set; } = [];
    public List<string> TargetHostIds { get; set; } = [];
    public bool IsEnabled { get; set; } = true;
    public bool SendTriggerTimeAsSeconds { get; set; }
    public TimecodeOffset? CueOffset { get; set; }
}
