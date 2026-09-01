namespace TimecodeBridge.Core.Models;

/// <summary>
/// Cross-platform representation of audio format (replaces NAudio.Wave.WaveFormat).
/// </summary>
public readonly struct WaveFormat
{
    public int SampleRate { get; }
    public int BitsPerSample { get; }
    public int Channels { get; }

    public WaveFormat(int sampleRate, int bitsPerSample, int channels)
    {
        SampleRate = sampleRate;
        BitsPerSample = bitsPerSample;
        Channels = channels;
    }

    public int BlockAlign => Channels * (BitsPerSample / 8);
    public int AverageBytesPerSecond => SampleRate * BlockAlign;
}
