using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
namespace TimecodeBridge.Tests.Services;

using TimecodeBridge.Core.Models;
using TimecodeBridge.Services;

public class LtcEncoderTests
{
    [Fact]
    public void Initialize_SetsWaveFormat()
    {
        var encoder = new LtcEncoder();
        encoder.Initialize(48000, FrameRate.Fps30);

        Assert.Equal(48000, encoder.WaveFormat.SampleRate);
        Assert.Equal(16, encoder.WaveFormat.BitsPerSample);
        Assert.Equal(1, encoder.WaveFormat.Channels);
    }

    [Fact]
    public void EnqueueFrame_WithoutInitialize_Throws()
    {
        var encoder = new LtcEncoder();
        var tc = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30);

        Assert.Throws<InvalidOperationException>(() => encoder.EnqueueFrame(tc));
    }

    [Fact]
    public void EnqueueFrame_ProducesNonSilentSamples()
    {
        var encoder = new LtcEncoder();
        encoder.Initialize(48000, FrameRate.Fps30);

        var tc = new TimecodeValue(1, 2, 3, 4, FrameRate.Fps30);
        encoder.EnqueueFrame(tc);

        // Read the samples
        var buffer = new byte[4000];
        int read = encoder.Read(buffer, 0, buffer.Length);
        Assert.Equal(buffer.Length, read);

        // Check that at least some samples are non-zero
        bool hasNonZero = false;
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] != 0) { hasNonZero = true; break; }
        }
        Assert.True(hasNonZero, "Encoded LTC should contain non-zero samples");
    }

    [Fact]
    public void Read_EmptyQueue_ReturnsSilence()
    {
        var encoder = new LtcEncoder();
        encoder.Initialize(48000, FrameRate.Fps30);

        var buffer = new byte[1000];
        int read = encoder.Read(buffer, 0, buffer.Length);

        Assert.Equal(buffer.Length, read);
        // All should be zeros (silence)
        Assert.All(buffer, b => Assert.Equal(0, b));
    }

    [Fact]
    public void VolumeLevel_ClampedToRange()
    {
        var encoder = new LtcEncoder();
        encoder.VolumeLevel = -0.5f;
        Assert.Equal(0f, encoder.VolumeLevel);

        encoder.VolumeLevel = 1.5f;
        Assert.Equal(1f, encoder.VolumeLevel);

        encoder.VolumeLevel = 0.5f;
        Assert.Equal(0.5f, encoder.VolumeLevel);
    }

    [Fact]
    public void Reset_ClearsQueue()
    {
        var encoder = new LtcEncoder();
        encoder.Initialize(48000, FrameRate.Fps30);
        encoder.EnqueueFrame(new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30));

        encoder.Reset();

        var buffer = new byte[1000];
        encoder.Read(buffer, 0, buffer.Length);
        // After reset, should output silence
        Assert.All(buffer, b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData(1, 2, 3, 4, false)]
    [InlineData(23, 59, 59, 29, false)]
    [InlineData(0, 1, 0, 2, true)]
    [InlineData(12, 34, 56, 17, false)]
    public void LtcEncodeRoundTrip_MatchesDecoder(int h, int m, int s, int f, bool dropFrame)
    {
        // This test verifies that encoding an LTC frame and then decoding it
        // recovers the original timecode value.
        var frameRate = dropFrame ? FrameRate.Fps2997Drop : FrameRate.Fps30;
        var original = new TimecodeValue(h, m, s, f, frameRate);
        var encoder = new LtcEncoder();
        int sampleRate = 48000;
        encoder.Initialize(sampleRate, frameRate);
        // Encode multiple frames: first acts as preamble for decoder sync
        encoder.EnqueueFrame(original);
        encoder.EnqueueFrame(original);
        encoder.EnqueueFrame(original);

        int fps = frameRate.FramesPerSecond();
        int samplesPerFrame = sampleRate / fps;
        // Read 3 frames' worth of samples
        var buffer = new byte[samplesPerFrame * 3 * 2];
        encoder.Read(buffer, 0, buffer.Length);

        // Feed to decoder
        var decoder = new LtcDecoder();
        decoder.Initialize(sampleRate, fps);

        TimecodeValue? decoded = null;
        decoder.FrameDecoded += (_, tc) => decoded = tc;

        // Convert our 16-bit PCM to the format the decoder expects
        decoder.ProcessSamples(buffer, buffer.Length, sampleRate, 16, 1);

        Assert.NotNull(decoded);
        Assert.Equal(original.Hours, decoded.Value.Hours);
        Assert.Equal(original.Minutes, decoded.Value.Minutes);
        Assert.Equal(original.Seconds, decoded.Value.Seconds);
        Assert.Equal(original.Frames, decoded.Value.Frames);
    }
}
