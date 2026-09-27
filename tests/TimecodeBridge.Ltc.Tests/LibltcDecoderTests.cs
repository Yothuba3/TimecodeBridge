using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Ltc;
using Xunit;

namespace TimecodeBridge.Ltc.Tests;

public class LibltcDecoderTests
{
    // 既存 LtcEncoder(16bit PCM モノラル)で LTC 信号を作り、指定フォーマットのバッファへ詰め替える
    private static byte[] Encode(int sampleRate, FrameRate rate, TimecodeValue start, int frames, float gain = 1f)
    {
        var enc = new LtcEncoder();
        enc.Initialize(sampleRate, rate);
        enc.VolumeLevel = 0.8f;
        long total = start.TotalFrames();
        for (int i = 0; i < frames; i++) enc.EnqueueFrame(TimecodeValue.FromTotalFrames(total + i, rate));
        int samplesPerFrame = sampleRate / rate.FramesPerSecond();
        var pcm = new byte[samplesPerFrame * 2 * frames];
        enc.Read(pcm, 0, pcm.Length);
        var f = new float[pcm.Length / 2];
        for (int i = 0; i < f.Length; i++) f[i] = BitConverter.ToInt16(pcm, i * 2) / 32768f * gain;
        return ToFloat32Bytes(f);
    }

    private static byte[] ToFloat32Bytes(float[] samples)
    {
        var bytes = new byte[samples.Length * 4];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static List<TimecodeValue> Decode(LibltcDecoder decoder, byte[] buffer, int sampleRate, int bits = 32, int channels = 1, bool isFloat = true, int chunkBytes = 4096)
    {
        var result = new List<TimecodeValue>();
        decoder.FrameDecoded += (_, tc) => result.Add(tc);
        for (int o = 0; o < buffer.Length; o += chunkBytes)
        {
            int n = Math.Min(chunkBytes, buffer.Length - o);
            var chunk = new byte[n];
            Array.Copy(buffer, o, chunk, 0, n);
            decoder.ProcessSamples(chunk, n, sampleRate, bits, channels, isFloat);
        }
        return result;
    }

    [Fact]
    public void Decodes30fpsSequenceInOrder()
    {
        using var d = new LibltcDecoder();
        d.Initialize(48000);
        var start = new TimecodeValue(1, 2, 3, 18, FrameRate.Fps30);
        var got = Decode(d, Encode(48000, FrameRate.Fps30, start, 12), 48000);

        // 末尾の 1 フレームは次の同期語を待つため未確定のまま終わる(libltc の仕様)
        Assert.InRange(got.Count, 11, 12);
        for (int i = 0; i < got.Count; i++)
        {
            var expected = TimecodeValue.FromTotalFrames(start.TotalFrames() + i, FrameRate.Fps30);
            Assert.Equal((expected.Hours, expected.Minutes, expected.Seconds, expected.Frames), (got[i].Hours, got[i].Minutes, got[i].Seconds, got[i].Frames));
        }
        Assert.Equal(FrameRate.Fps30, got[^1].FrameRate);
        Assert.NotNull(d.LastVolumeDbfs);
    }

    [Fact]
    public void Decodes25fpsAndReports25()
    {
        using var d = new LibltcDecoder();
        d.Initialize(48000);
        // 24 コマ目が確定するには後続フレームが必要なので、秒をまたいで 14 フレーム流す(12..24, 00)
        var got = Decode(d, Encode(48000, FrameRate.Fps25, new TimecodeValue(0, 0, 10, 12, FrameRate.Fps25), 14), 48000);
        Assert.InRange(got.Count, 13, 14);
        Assert.Equal(FrameRate.Fps25, got[^1].FrameRate);
    }

    [Fact]
    public void DropFrameFlagYields2997Drop()
    {
        using var d = new LibltcDecoder();
        d.Initialize(48000);
        var got = Decode(d, Encode(48000, FrameRate.Fps2997Drop, new TimecodeValue(0, 1, 0, 2, FrameRate.Fps2997Drop), 12), 48000);
        Assert.InRange(got.Count, 11, 12);
        Assert.All(got, tc => Assert.Equal(FrameRate.Fps2997Drop, tc.FrameRate));
    }

    [Fact]
    public void Decodes96kHz()
    {
        using var d = new LibltcDecoder();
        d.Initialize(96000);
        var got = Decode(d, Encode(96000, FrameRate.Fps30, new TimecodeValue(2, 0, 0, 0, FrameRate.Fps30), 12), 96000);
        Assert.InRange(got.Count, 11, 12);
    }

    [Fact]
    public void NonFiniteSamplesDoNotStopDecoding()
    {
        using var d = new LibltcDecoder();
        d.Initialize(48000);
        var poison = new float[9600];
        for (int i = 0; i < poison.Length; i++) poison[i] = (i % 3) switch { 0 => float.NaN, 1 => float.PositiveInfinity, _ => float.NegativeInfinity };
        var poisonBytes = ToFloat32Bytes(poison);
        d.ProcessSamples(poisonBytes, poisonBytes.Length, 48000, 32, 1);

        // 無音(0 に置換された区間)の直後は libltc の閾値追従が戻るまで先頭 1〜2 フレームを取りこぼすが、
        // その後は最後まで復号が続くこと(=永久停止しないこと)を確かめる
        var got = Decode(d, Encode(48000, FrameRate.Fps30, new TimecodeValue(3, 0, 0, 0, FrameRate.Fps30), 12), 48000);
        Assert.InRange(got.Count, 9, 12);
        Assert.Equal((3, 0, 0, 10), (got[^1].Hours, got[^1].Minutes, got[^1].Seconds, got[^1].Frames));
    }

    [Fact]
    public void OverdrivenSignalIsClampedAndStillDecodes()
    {
        using var d = new LibltcDecoder();
        d.Initialize(48000);
        var got = Decode(d, Encode(48000, FrameRate.Fps30, new TimecodeValue(4, 0, 0, 0, FrameRate.Fps30), 12, gain: 4f), 48000);
        Assert.InRange(got.Count, 11, 12);
    }

    [Fact]
    public void Pcm16SixChannelInputUsesFirstChannel()
    {
        using var d = new LibltcDecoder();
        d.Initialize(48000);
        var mono = Encode(48000, FrameRate.Fps30, new TimecodeValue(5, 0, 0, 0, FrameRate.Fps30), 12);
        int n = mono.Length / 4;
        const int ch = 6;
        var interleaved = new byte[n * ch * 2];
        for (int i = 0; i < n; i++)
        {
            short s = (short)(Math.Clamp(BitConverter.ToSingle(mono, i * 4), -1f, 1f) * 32767f);
            BitConverter.TryWriteBytes(interleaved.AsSpan(i * ch * 2, 2), s);
            // 他チャンネルには無関係なノイズを入れて、先頭チャンネルだけが使われることを確かめる
            for (int c = 1; c < ch; c++) BitConverter.TryWriteBytes(interleaved.AsSpan((i * ch + c) * 2, 2), (short)((i * 7919 + c) % 20000 - 10000));
        }
        var got = Decode(d, interleaved, 48000, bits: 16, channels: ch, isFloat: false, chunkBytes: 6000);
        Assert.InRange(got.Count, 11, 12);
    }

    [Fact]
    public void Int32PcmInputIsInterpretedAsInteger()
    {
        using var d = new LibltcDecoder();
        d.Initialize(48000);
        var mono = Encode(48000, FrameRate.Fps30, new TimecodeValue(6, 0, 0, 0, FrameRate.Fps30), 12);
        var ints = new byte[mono.Length];
        for (int i = 0; i < mono.Length / 4; i++)
            BitConverter.TryWriteBytes(ints.AsSpan(i * 4, 4), (int)(Math.Clamp(BitConverter.ToSingle(mono, i * 4), -1f, 1f) * int.MaxValue));
        var got = Decode(d, ints, 48000, bits: 32, isFloat: false);
        Assert.InRange(got.Count, 11, 12);
    }

    [Fact]
    public void OddByteCountsAndBadArgumentsAreIgnored()
    {
        using var d = new LibltcDecoder();
        d.Initialize(48000);
        var buf = new byte[7];
        d.ProcessSamples(buf, 7, 48000, 32, 1);
        d.ProcessSamples(buf, 100, 48000, 32, 1);
        d.ProcessSamples(buf, -1, 48000, 32, 1);
        d.ProcessSamples(buf, 4, 48000, 24, 1);
        d.ProcessSamples(buf, 4, 48000, 32, 0);
    }

    [Fact]
    public void ProcessAfterDisposeIsNoop()
    {
        var d = new LibltcDecoder();
        d.Initialize(48000);
        int count = 0;
        d.FrameDecoded += (_, _) => count++;
        d.Dispose();
        var signal = Encode(48000, FrameRate.Fps30, new TimecodeValue(7, 0, 0, 0, FrameRate.Fps30), 4);
        d.ProcessSamples(signal, signal.Length, 48000, 32, 1);
        d.Dispose();
        Assert.Equal(0, count);
        Assert.Throws<ObjectDisposedException>(() => d.Initialize(48000));
    }

    [Fact]
    public void ProcessBeforeInitializeIsNoop()
    {
        using var d = new LibltcDecoder();
        var signal = Encode(48000, FrameRate.Fps30, new TimecodeValue(8, 0, 0, 0, FrameRate.Fps30), 4);
        d.ProcessSamples(signal, signal.Length, 48000, 32, 1);
        Assert.False(d.IsInitialized);
    }

    [Fact]
    public void ReinitializeResetsFrameRateEstimate()
    {
        using var d = new LibltcDecoder();
        d.Initialize(48000);
        Decode(d, Encode(48000, FrameRate.Fps30, new TimecodeValue(0, 0, 0, 20, FrameRate.Fps30), 12), 48000);
        d.Initialize(48000);
        var got = Decode(d, Encode(48000, FrameRate.Fps24, new TimecodeValue(0, 0, 0, 0, FrameRate.Fps24), 12), 48000);
        Assert.InRange(got.Count, 11, 12);
        Assert.Equal(FrameRate.Fps24, got[^1].FrameRate);
    }
}

public class LibltcDecoderLongRunTests
{
    [Fact]
    public void TenSecondsInSmallChunksDecodesEveryFrameWithoutCorruption()
    {
        // CoreAudio のコールバック相当(512 サンプル)で 10 秒分を流し、欠落・化け(時・分・秒・フレームの範囲外や逆行)がないこと
        const int sr = 48000, frames = 300;
        var enc = new LtcEncoder();
        enc.Initialize(sr, FrameRate.Fps30);
        enc.VolumeLevel = 0.8f;
        var start = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30);
        for (int i = 0; i < frames; i++) enc.EnqueueFrame(TimecodeValue.FromTotalFrames(start.TotalFrames() + i, FrameRate.Fps30));
        var pcm = new byte[sr / 30 * 2 * frames];
        enc.Read(pcm, 0, pcm.Length);

        using var d = new LibltcDecoder();
        d.Initialize(sr);
        var got = new List<TimecodeValue>();
        d.FrameDecoded += (_, tc) => got.Add(tc);

        const int chunk = 512;
        var buf = new byte[chunk * 4];
        for (int o = 0; o + chunk <= pcm.Length / 2; o += chunk)
        {
            for (int i = 0; i < chunk; i++)
                BitConverter.TryWriteBytes(buf.AsSpan(i * 4, 4), BitConverter.ToInt16(pcm, (o + i) * 2) / 32768f);
            d.ProcessSamples(buf, buf.Length, sr, 32, 1);
        }

        Assert.InRange(got.Count, frames - 2, frames);
        for (int i = 0; i < got.Count; i++)
        {
            var expected = TimecodeValue.FromTotalFrames(start.TotalFrames() + i, FrameRate.Fps30);
            Assert.Equal(expected.ToString(), got[i].ToString());
        }
    }
}
