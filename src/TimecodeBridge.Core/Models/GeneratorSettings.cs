namespace TimecodeBridge.Core.Models;

public class GeneratorSettings
{
    public FrameRate FrameRate { get; set; } = FrameRate.Fps30;
    public TimecodeValue StartTime { get; set; } = new(0, 0, 0, 0, FrameRate.Fps30);

    /// <summary>ループの終端。null なら開始時刻から進み続ける。</summary>
    public TimecodeValue? LoopEndTime { get; set; }
    public string OutputDeviceId { get; set; } = string.Empty;
    public float VolumeLevel { get; set; } = 0.8f;
}
