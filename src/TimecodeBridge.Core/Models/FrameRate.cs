namespace TimecodeBridge.Core.Models;

public enum FrameRate
{
    Fps24,
    Fps25,
    Fps2997Drop,
    Fps30,
}

public static class FrameRateExtensions
{
    public static int FramesPerSecond(this FrameRate frameRate) => frameRate switch
    {
        FrameRate.Fps24 => 24,
        FrameRate.Fps25 => 25,
        FrameRate.Fps2997Drop => 30,
        FrameRate.Fps30 => 30,
        _ => throw new ArgumentOutOfRangeException(nameof(frameRate)),
    };

    public static bool IsDropFrame(this FrameRate frameRate) =>
        frameRate == FrameRate.Fps2997Drop;
}
