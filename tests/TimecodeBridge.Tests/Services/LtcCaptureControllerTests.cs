using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Services;

namespace TimecodeBridge.Tests.Services;

public class LtcCaptureControllerTests : IDisposable
{
    private readonly LtcCaptureController _controller;

    public LtcCaptureControllerTests()
    {
        _controller = new LtcCaptureController();
    }

    public void Dispose()
    {
        _controller.Dispose();
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        _controller.Dispose();
        _controller.Dispose(); // double-dispose safety
    }

    [Fact]
    public void Stop_BeforeStart_DoesNotThrow()
    {
        _controller.Stop();
    }

    [Fact]
    public void OnFrameDecoded_DefaultsToNull()
    {
        Assert.Null(_controller.OnFrameDecoded);
    }

    [Fact]
    public void OnAudioSamplesAvailable_DefaultsToNull()
    {
        Assert.Null(_controller.OnAudioSamplesAvailable);
    }

    [Fact]
    public void ExtractMonoSamples_Float32Mono_ReturnsCorrectSamples()
    {
        // Arrange: 3 float32 mono samples
        var sample1 = BitConverter.GetBytes(0.5f);
        var sample2 = BitConverter.GetBytes(-0.25f);
        var sample3 = BitConverter.GetBytes(1.0f);
        var buffer = new byte[12];
        Array.Copy(sample1, 0, buffer, 0, 4);
        Array.Copy(sample2, 0, buffer, 4, 4);
        Array.Copy(sample3, 0, buffer, 8, 4);

        // Act
        var result = LtcCaptureController.ExtractMonoSamples(buffer, 12, bitsPerSample: 32, channels: 1);

        // Assert
        Assert.Equal(3, result.Length);
        Assert.Equal(0.5f, result[0], precision: 5);
        Assert.Equal(-0.25f, result[1], precision: 5);
        Assert.Equal(1.0f, result[2], precision: 5);
    }

    [Fact]
    public void ExtractMonoSamples_Float32Stereo_ReturnsFirstChannelOnly()
    {
        // Arrange: 2 stereo frames (L=0.5, R=0.1, L=-0.3, R=0.9)
        var l1 = BitConverter.GetBytes(0.5f);
        var r1 = BitConverter.GetBytes(0.1f);
        var l2 = BitConverter.GetBytes(-0.3f);
        var r2 = BitConverter.GetBytes(0.9f);
        var buffer = new byte[16];
        Array.Copy(l1, 0, buffer, 0, 4);
        Array.Copy(r1, 0, buffer, 4, 4);
        Array.Copy(l2, 0, buffer, 8, 4);
        Array.Copy(r2, 0, buffer, 12, 4);

        // Act
        var result = LtcCaptureController.ExtractMonoSamples(buffer, 16, bitsPerSample: 32, channels: 2);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal(0.5f, result[0], precision: 5);
        Assert.Equal(-0.3f, result[1], precision: 5);
    }

    [Fact]
    public void ExtractMonoSamples_Pcm16Mono_ReturnsNormalizedSamples()
    {
        // Arrange: 2 PCM16 mono samples (max positive = 32767, max negative = -32768)
        var buffer = new byte[4];
        BitConverter.GetBytes((short)16384).CopyTo(buffer, 0);   // ~0.5
        BitConverter.GetBytes((short)-16384).CopyTo(buffer, 2);  // ~-0.5

        // Act
        var result = LtcCaptureController.ExtractMonoSamples(buffer, 4, bitsPerSample: 16, channels: 1);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal(16384f / 32768f, result[0], precision: 5);
        Assert.Equal(-16384f / 32768f, result[1], precision: 5);
    }

    [Fact]
    public void ExtractMonoSamples_UnsupportedBitsPerSample_ReturnsZeros()
    {
        // Arrange: 8-bit samples (unsupported)
        var buffer = new byte[] { 128, 64 };

        // Act
        var result = LtcCaptureController.ExtractMonoSamples(buffer, 2, bitsPerSample: 8, channels: 1);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.All(result, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void ExtractMonoSamples_EmptyBuffer_ReturnsEmptyArray()
    {
        var result = LtcCaptureController.ExtractMonoSamples(Array.Empty<byte>(), 0, bitsPerSample: 32, channels: 1);
        Assert.Empty(result);
    }
}
