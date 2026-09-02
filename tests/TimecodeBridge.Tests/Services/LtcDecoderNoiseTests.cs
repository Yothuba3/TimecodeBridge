namespace TimecodeBridge.Tests.Services;

using TimecodeBridge.Models;
using TimecodeBridge.Services;

public class LtcDecoderNoiseTests
{
    private const int SampleRate = 48000;

    [Fact]
    public void ノイズ音声の後でも実信号だけを解析する()
    {
        var decoder = new LtcDecoder();
        decoder.Initialize(SampleRate, 30);
        var decoded = new List<TimecodeValue>();
        decoder.FrameDecoded += (_, tc) => decoded.Add(tc);

        // 長時間の無信号を模したノイズ（ランダム波形、float32）
        var rng = new Random(12345);
        var noise = new byte[SampleRate * 2 * sizeof(float)];
        for (int i = 0; i < noise.Length / sizeof(float); i++)
        {
            var v = (float)(rng.NextDouble() * 2 - 1) * 0.3f;
            BitConverter.GetBytes(v).CopyTo(noise, i * sizeof(float));
        }
        decoder.ProcessSamples(noise, noise.Length, SampleRate, 32, 1);

        // その後に実信号（PCM16）
        var encoder = new LtcEncoder();
        encoder.Initialize(SampleRate, FrameRate.Fps30);
        var expected = new HashSet<long>();
        for (int i = 0; i < 8; i++)
        {
            var tc = new TimecodeValue(10, 20, 30, i, FrameRate.Fps30);
            expected.Add(tc.ToOrdinal());
            encoder.EnqueueFrame(tc);
        }
        var signal = new byte[SampleRate / 30 * 8 * 2];
        encoder.Read(signal, 0, signal.Length);
        decoder.ProcessSamples(signal, signal.Length, SampleRate, 16, 1);

        Assert.NotEmpty(decoded);
        // ノイズ由来のガベージが混ざらず、送った実フレームのみが解析される
        Assert.All(decoded, tc => Assert.Contains(tc.ToOrdinal(), expected));
    }
}
