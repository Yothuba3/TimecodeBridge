namespace TimecodeBridge.Core.Models;

public readonly record struct TimecodeValue(
    int Hours,
    int Minutes,
    int Seconds,
    int Frames,
    FrameRate FrameRate) : IComparable<TimecodeValue>
{
    public long TotalFrames()
    {
        int fps = FrameRate.FramesPerSecond();

        if (FrameRate.IsDropFrame())
        {
            // Drop frame: 29.97fps drops frames 0,1 at each minute except every 10th
            long totalMinutes = Hours * 60L + Minutes;
            long dropEvents = totalMinutes - (totalMinutes / 10);
            return Hours * 3600L * fps
                 + Minutes * 60L * fps
                 + Seconds * fps
                 + Frames
                 - 2 * dropEvents;
        }

        return Hours * 3600L * fps
             + Minutes * 60L * fps
             + Seconds * fps
             + Frames;
    }

    public static TimecodeValue FromTotalFrames(long totalFrames, FrameRate frameRate)
    {
        if (totalFrames < 0) totalFrames = 0;

        int fps = frameRate.FramesPerSecond();

        if (frameRate.IsDropFrame())
        {
            // Reverse drop frame calculation
            long framesPerMinute = fps * 60L - 2;            // 1798
            long framesPer10Min = framesPerMinute * 10 + 2;  // 17982

            long tenMinBlocks = totalFrames / framesPer10Min;
            long remainder = totalFrames % framesPer10Min;

            long minutesInRemainder;
            if (remainder < fps * 60L)
            {
                // First minute of 10-min block (no drop)
                minutesInRemainder = 0;
            }
            else
            {
                // Remaining 9 minutes have drops
                minutesInRemainder = 1 + (remainder - fps * 60L) / framesPerMinute;
            }

            long totalMinutes = tenMinBlocks * 10 + minutesInRemainder;
            long dropEvents = totalMinutes - (totalMinutes / 10);
            long adjustedFrames = totalFrames + 2 * dropEvents;

            int h = (int)(adjustedFrames / (fps * 3600L));
            adjustedFrames %= fps * 3600L;
            int m = (int)(adjustedFrames / (fps * 60L));
            adjustedFrames %= fps * 60L;
            int s = (int)(adjustedFrames / fps);
            int f = (int)(adjustedFrames % fps);

            return new TimecodeValue(h, m, s, f, frameRate);
        }

        int hours = (int)(totalFrames / (fps * 3600L));
        totalFrames %= fps * 3600L;
        int minutes = (int)(totalFrames / (fps * 60L));
        totalFrames %= fps * 60L;
        int seconds = (int)(totalFrames / fps);
        int frames = (int)(totalFrames % fps);

        return new TimecodeValue(hours, minutes, seconds, frames, frameRate);
    }

    public TimecodeValue Add(TimecodeOffset offset)
    {
        long resultFrames = TotalFrames() + offset.TotalFrames();
        if (resultFrames < 0) resultFrames = 0;
        return FromTotalFrames(resultFrames, FrameRate);
    }

    public int CompareTo(TimecodeValue other) =>
        TotalFrames().CompareTo(other.TotalFrames());

    public static bool operator <(TimecodeValue left, TimecodeValue right) =>
        left.CompareTo(right) < 0;

    public static bool operator >(TimecodeValue left, TimecodeValue right) =>
        left.CompareTo(right) > 0;

    public static bool operator <=(TimecodeValue left, TimecodeValue right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >=(TimecodeValue left, TimecodeValue right) =>
        left.CompareTo(right) >= 0;

    /// <summary>
    /// Frame-rate-independent ordinal for HH:MM:SS:FF comparison.
    /// Uses 30 as the fixed base to avoid fluctuations from inferred frame rates.
    /// </summary>
    public long ToOrdinal()
        => Hours * 108_000L + Minutes * 1_800L + Seconds * 30L + Frames;

    public override string ToString()
    {
        string separator = FrameRate.IsDropFrame() ? ";" : ":";
        return $"{Hours:D2}:{Minutes:D2}:{Seconds:D2}{separator}{Frames:D2}";
    }
}
