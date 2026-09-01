namespace TimecodeBridge.Core.Models;

public readonly record struct TimecodeOffset(
    bool IsNegative,
    int Hours,
    int Minutes,
    int Seconds,
    int Frames,
    FrameRate FrameRate)
{
    public long TotalFrames()
    {
        int fps = FrameRate.FramesPerSecond();
        long frames = Hours * 3600L * fps
                    + Minutes * 60L * fps
                    + Seconds * fps
                    + Frames;
        return IsNegative ? -frames : frames;
    }

    public static TimecodeOffset Zero(FrameRate frameRate) =>
        new(false, 0, 0, 0, 0, frameRate);

    public override string ToString()
    {
        string sign = IsNegative ? "-" : "+";
        return $"{sign}{Hours:D2}:{Minutes:D2}:{Seconds:D2}:{Frames:D2}";
    }

    public static bool TryParse(string text, FrameRate frameRate, out TimecodeOffset result)
    {
        result = Zero(frameRate);
        if (string.IsNullOrWhiteSpace(text)) return false;

        text = text.Trim();
        bool isNegative = false;

        if (text[0] == '-')
        {
            isNegative = true;
            text = text[1..];
        }
        else if (text[0] == '+' || text[0] == '±')
        {
            text = text[1..];
        }

        var parts = text.Split(':');
        if (parts.Length != 4) return false;

        if (!int.TryParse(parts[0], out var h) || h < 0 || h > 23) return false;
        if (!int.TryParse(parts[1], out var m) || m < 0 || m > 59) return false;
        if (!int.TryParse(parts[2], out var s) || s < 0 || s > 59) return false;
        if (!int.TryParse(parts[3], out var f) || f < 0 || f >= frameRate.FramesPerSecond()) return false;

        result = new TimecodeOffset(isNegative, h, m, s, f, frameRate);
        return true;
    }
}
