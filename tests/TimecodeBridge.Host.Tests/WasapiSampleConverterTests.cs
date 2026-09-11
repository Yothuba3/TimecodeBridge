using NAudio.Wave;
using TimecodeBridge.Windows.Services;
using Xunit;

namespace TimecodeBridge.Host.Tests;

public class WasapiSampleConverterTests
{
    [Fact]
    public void Pcm16StereoTakesFirstChannel()
    {
        var buf = new byte[8];
        BitConverter.TryWriteBytes(buf.AsSpan(0, 2), (short)16384);   // L
        BitConverter.TryWriteBytes(buf.AsSpan(2, 2), (short)-32768);  // R
        BitConverter.TryWriteBytes(buf.AsSpan(4, 2), (short)-16384);  // L
        var f = WasapiSampleConverter.ToMonoFloat(buf, 8, new WaveFormat(48000, 16, 2));
        Assert.Equal(new[] { 0.5f, -0.5f }, f);
    }

    [Fact]
    public void Float32IsPassedThroughAndInt32IsScaled()
    {
        var floatBuf = new byte[4];
        BitConverter.TryWriteBytes(floatBuf, 0.25f);
        Assert.Equal(new[] { 0.25f }, WasapiSampleConverter.ToMonoFloat(floatBuf, 4, WaveFormat.CreateIeeeFloatWaveFormat(48000, 1)));

        var intBuf = new byte[4];
        BitConverter.TryWriteBytes(intBuf, int.MinValue / 2);
        Assert.Equal(new[] { -0.5f }, WasapiSampleConverter.ToMonoFloat(intBuf, 4, new WaveFormat(48000, 32, 1)));
    }

    [Fact]
    public void NonFiniteFloatBecomesZeroAndPartialFramesAreDropped()
    {
        var buf = new byte[7];
        BitConverter.TryWriteBytes(buf.AsSpan(0, 4), float.NaN);
        var f = WasapiSampleConverter.ToMonoFloat(buf, 7, WaveFormat.CreateIeeeFloatWaveFormat(48000, 1));
        Assert.Equal(new[] { 0f }, f);
        Assert.Empty(WasapiSampleConverter.ToMonoFloat(buf, 0, WaveFormat.CreateIeeeFloatWaveFormat(48000, 1)));
    }
}
