namespace TimecodeBridge.Core.Models;

public enum TimecodeSourceType
{
    Ltc,
    Generator,
}

public class TimecodeSourceSettings
{
    public TimecodeSourceType SourceType { get; set; } = TimecodeSourceType.Ltc;
    public string DeviceId { get; set; } = string.Empty;
    public GeneratorSettings GeneratorSettings { get; set; } = new();
    public double FreerunDurationSeconds { get; set; }
}
