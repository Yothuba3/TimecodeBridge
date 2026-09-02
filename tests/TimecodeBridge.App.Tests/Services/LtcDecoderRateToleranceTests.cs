using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;

namespace TimecodeBridge.App.Tests.Services;

public class LtcDecoderRateToleranceTests
{
    private const int SampleRate = 48000;

    public static IEnumerable<object[]> RateCombinations()
    {
        foreach (var input in new[] { FrameRate.Fps24, FrameRate.Fps25, FrameRate.Fps2997Drop, FrameRate.Fps30 })
        {
            foreach (var configured in new[] { 24, 25, 30 })
            {
                yield return [input, configured];
            }
        }
    }

    [Theory]
    [MemberData(nameof(RateCombinations))]
    public void 設定レートに関係なく全レートのLTCをデコードできる(FrameRate input, int configuredFps)
    {
        var encoder = new LtcEncoder();
        encoder.Initialize(SampleRate, input);
        var expected = new List<TimecodeValue>();
        for (int i = 0; i < 10; i++)
        {
            var frame = TimecodeValue.FromTotalFrames(3600L * 30 + i, input);
            expected.Add(frame);
            encoder.EnqueueFrame(frame);
        }
        var audio = new byte[SampleRate * 2];
        encoder.Read(audio, 0, audio.Length);

        var decoder = new LtcDecoder();
        decoder.Initialize(SampleRate, configuredFps);
        var decoded = new List<TimecodeValue>();
        decoder.FrameDecoded += (_, tc) => decoded.Add(tc);
        decoder.ProcessSamples(audio, audio.Length, SampleRate, 16, 1);

        // 先頭フレームは同期語より前の部分が無いため取れない。残りは全て一致する
        Assert.Equal(9, decoded.Count);
        for (int i = 0; i < decoded.Count; i++)
        {
            var e = expected[i];
            Assert.Equal((e.Hours, e.Minutes, e.Seconds, e.Frames), (decoded[i].Hours, decoded[i].Minutes, decoded[i].Seconds, decoded[i].Frames));
            Assert.Equal(input.IsDropFrame(), decoded[i].FrameRate.IsDropFrame());
        }
    }
}
