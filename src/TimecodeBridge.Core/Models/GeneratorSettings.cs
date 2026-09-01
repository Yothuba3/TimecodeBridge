namespace TimecodeBridge.Core.Models;

public class GeneratorSettings
{
    public FrameRate FrameRate { get; set; } = FrameRate.Fps30;
    public TimecodeValue StartTime { get; set; } = new(0, 0, 0, 0, FrameRate.Fps30);
    public string OutputDeviceId { get; set; } = string.Empty;
    public float VolumeLevel { get; set; } = 0.8f;
}
