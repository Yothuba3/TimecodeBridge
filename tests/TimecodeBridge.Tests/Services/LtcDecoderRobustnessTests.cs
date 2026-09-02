using TimecodeBridge.Models;
using TimecodeBridge.Services;

namespace TimecodeBridge.Tests.Services;

/// <summary>
/// 劣化したLTC音声に対するデコーダの堅牢性を固定するテスト。
/// 「他ソフトは拾えるのにこのアプリだけ止まる」の原因だった、局所グリッチの伝播と
/// 複合劣化（弱信号＋DC＋ノイズ）での全滅が再発しないことを保証する。
/// </summary>
public class LtcDecoderRobustnessTests
{
    private const int Sr = 48000;

    private static (float[] signal, int length) MakeCleanLtc(int frames)
    {
        var enc = new LtcEncoder();
        enc.Initialize(Sr, FrameRate.Fps30);
        for (int i = 0; i < frames; i++)
            enc.EnqueueFrame(new TimecodeValue(10, 0, i / 30, i % 30, FrameRate.Fps30));
        var buf = new byte[Sr * 2 * (frames / 30 + 3)];
        enc.Read(buf, 0, buf.Length);
        int n = buf.Length / 2;
        var f = new float[n];
        for (int i = 0; i < n; i++) f[i] = BitConverter.ToInt16(buf, i * 2) / 32768f;
        int last = n - 1;
        while (last > 0 && Math.Abs(f[last]) < 1e-6) last--;
        return (f, last + 1);
    }

    private static int Decode(float[] signal, int length)
    {
        var dec = new LtcDecoder();
        dec.Initialize(Sr);
        int decoded = 0;
        dec.FrameDecoded += (_, _) => decoded++;
        var b = new byte[length * 2];
        for (int i = 0; i < length; i++)
        {
            short s = (short)(Math.Clamp(signal[i], -1f, 1f) * 32767);
            b[i * 2] = (byte)(s & 0xFF);
            b[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        dec.ProcessSamples(b, b.Length, Sr, 16, 1);
        return decoded;
    }

    [Fact]
    public void クリーン信号はほぼ全フレームをデコードする()
    {
        var (sig, len) = MakeCleanLtc(300);
        // 先頭1フレームは同期に必要な前フレームが無いため取れない
        Assert.True(Decode(sig, len) >= 299);
    }

    [Fact]
    public void 周期的グリッチがあっても大半のフレームをデコードできる()
    {
        var (sig, len) = MakeCleanLtc(300);
        var rnd = new Random(7);
        int frameLen = Sr / 30, bitLen = frameLen / 80;
        // 1フレームごとに破壊的グリッチを入れる（旧デコーダはここで数フレームしか取れなかった）
        for (int fr = 0; fr < 300; fr++)
        {
            int pos = fr * frameLen + rnd.Next(frameLen);
            for (int k = 0; k < bitLen * 2 && pos + k < len; k++)
                sig[pos + k] = (float)(rnd.NextDouble() * 2 - 1);
        }
        // 旧実装は 300 中わずか数フレーム。修正後は大半を拾える。
        Assert.True(Decode(sig, len) >= 150, "グリッチ頻発時にデコードが激減している（グリッチ伝播の退行）");
    }

    [Fact]
    public void 弱信号にDCとノイズが重なってもデコードできる()
    {
        var (sig, len) = MakeCleanLtc(300);
        var rnd = new Random(11);
        // 振幅±0.03、DC+0.02、ノイズ0.01。旧デコーダはこの複合条件で0フレームだった。
        for (int i = 0; i < len; i++)
            sig[i] = (float)Math.Clamp(sig[i] * 0.03 + 0.02 + (rnd.NextDouble() * 2 - 1) * 0.01, -1, 1);
        Assert.True(Decode(sig, len) >= 200, "弱信号＋DC＋ノイズでデコードできていない（フロントエンドの退行）");
    }

    [Fact]
    public void 大きなDCとノイズが重なってもデコードできる()
    {
        var (sig, len) = MakeCleanLtc(300);
        var rnd = new Random(13);
        for (int i = 0; i < len; i++)
            sig[i] = (float)Math.Clamp(sig[i] * 0.2 + 0.1 + (rnd.NextDouble() * 2 - 1) * 0.1, -1, 1);
        Assert.True(Decode(sig, len) >= 250);
    }

    private static (byte[] pcm, int frames) MakeStereoLtcPcm(int startSec, int frames, int ltcChannel, int channels)
    {
        var enc = new LtcEncoder();
        enc.Initialize(Sr, FrameRate.Fps30);
        for (int i = 0; i < frames; i++)
            enc.EnqueueFrame(new TimecodeValue(10, 0, startSec + i / 30, i % 30, FrameRate.Fps30));
        var mono = new byte[Sr * 2 * (frames / 30 + 2)];
        enc.Read(mono, 0, mono.Length);
        int n = mono.Length / 2;
        int last = n - 1;
        while (last > 0 && mono[last * 2] == 0 && mono[last * 2 + 1] == 0) last--;
        n = last + 1;
        var outb = new byte[n * 2 * channels];
        for (int i = 0; i < n; i++)
            for (int c = 0; c < channels; c++)
            {
                int di = (i * channels + c) * 2;
                if (c == ltcChannel) { outb[di] = mono[i * 2]; outb[di + 1] = mono[i * 2 + 1]; }
            }
        return (outb, frames);
    }

    [Fact]
    public void ステレオ入力をチャンネル境界に揃わないチャンクで供給しても止まらない()
    {
        // 「一度認識しなくなると再接続まで一生引っかからない」の本命原因の回帰テスト。
        // ステレオで、チャンネルフレーム境界(4byte)に揃わない端数チャンクを挟むと、
        // 旧実装はデインターリーブ位相が固定でずれて以降永久にデコードできなくなった。
        var (data, _) = MakeStereoLtcPcm(0, 300, ltcChannel: 0, channels: 2);
        var dec = new LtcDecoder();
        dec.Initialize(Sr);
        int decoded = 0;
        long secondHalfOrd = new TimecodeValue(10, 0, 5, 0, FrameRate.Fps30).ToOrdinal();
        int afterOdd = 0;
        dec.FrameDecoded += (_, tc) => { decoded++; if (tc.ToOrdinal() >= secondHalfOrd) afterOdd++; };

        // 前半を境界通りに、途中で2byte(1サンプル=半チャンネルフレーム)だけの端数を1回挟む
        int half = data.Length / 2; half -= half % 4;
        var a = new byte[half]; Array.Copy(data, 0, a, 0, half);
        dec.ProcessSamples(a, a.Length, Sr, 16, 2);
        var odd = new byte[2]; Array.Copy(data, half, odd, 0, 2);
        dec.ProcessSamples(odd, 2, Sr, 16, 2);
        var b = new byte[data.Length - half - 2]; Array.Copy(data, half + 2, b, 0, b.Length);
        dec.ProcessSamples(b, b.Length, Sr, 16, 2);

        // 端数混入の後も後半をデコードし続けられること（旧実装はここが0だった）
        Assert.True(afterOdd > 100, $"端数チャンク後にデコードが止まっている（afterOdd={afterOdd}）");
    }

    [Theory]
    [InlineData(1, 16)]
    [InlineData(2, 16)]
    [InlineData(6, 16)]
    [InlineData(1, 32)]
    [InlineData(2, 32)]
    [InlineData(4, 32)]
    [InlineData(6, 32)] // UR44等の多入力IFが6ch×32bitで見えるケース。frameBytes=24 で読めなくなっていた。
    public void 多チャンネル多ビット深度の入力でも先頭chのLTCをデコードできる(int channels, int bits)
    {
        int frames = 150;
        var enc = new LtcEncoder();
        enc.Initialize(Sr, FrameRate.Fps30);
        for (int i = 0; i < frames; i++)
            enc.EnqueueFrame(new TimecodeValue(10, 0, i / 30, i % 30, FrameRate.Fps30));
        var mono16 = new byte[Sr * 2 * (frames / 30 + 2)];
        enc.Read(mono16, 0, mono16.Length);
        int n = mono16.Length / 2;
        int last = n - 1;
        while (last > 0 && mono16[last * 2] == 0 && mono16[last * 2 + 1] == 0) last--;
        n = last + 1;

        int bps = bits / 8;
        var interleaved = new byte[n * bps * channels];
        for (int i = 0; i < n; i++)
        {
            short s = BitConverter.ToInt16(mono16, i * 2);
            int di = (i * channels + 0) * bps; // 先頭チャンネルにLTC
            if (bits == 16) { interleaved[di] = (byte)(s & 0xFF); interleaved[di + 1] = (byte)((s >> 8) & 0xFF); }
            else BitConverter.GetBytes(s / 32768f).CopyTo(interleaved, di);
        }

        var dec = new LtcDecoder();
        dec.Initialize(Sr);
        int decoded = 0;
        dec.FrameDecoded += (_, _) => decoded++;
        dec.ProcessSamples(interleaved, interleaved.Length, Sr, bits, channels);

        Assert.True(decoded >= frames - 1, $"{channels}ch {bits}bit でデコードできていない（decoded={decoded}）");
    }

    [Fact]
    public void 繰り越し中にフォーマットが変わっても例外にならず新フォーマットで受信できる()
    {
        var dec = new LtcDecoder();
        dec.Initialize(Sr);
        int decoded = 0;
        dec.FrameDecoded += (_, _) => decoded++;

        // 6ch×32bit(=24byte/frame)の端数23byteだけ渡して繰り越しを作る
        var (six, _) = MakeStereoLtcPcm(0, 30, ltcChannel: 0, channels: 6); // 32bit相当ではないが端数を作る目的
        var partial = new byte[23];
        Array.Copy(six, 0, partial, 0, Math.Min(23, six.Length));
        dec.ProcessSamples(partial, 23, Sr, 32, 6);

        // フォーマットをmono16へ変えて呼ぶ → 例外にならず、以後mono16を受信できる
        var (mono, _) = MakeStereoLtcPcm(0, 150, ltcChannel: 0, channels: 1);
        var ex = Record.Exception(() => dec.ProcessSamples(mono, mono.Length, Sr, 16, 1));
        Assert.Null(ex);
        Assert.True(decoded > 100);
    }

    [Fact]
    public void bytesRecordedが配列長を超えても負でも例外にならない()
    {
        var dec = new LtcDecoder();
        dec.Initialize(Sr);
        var buf = new byte[10];

        Assert.Null(Record.Exception(() => dec.ProcessSamples(buf, 1000, Sr, 16, 1)));
        Assert.Null(Record.Exception(() => dec.ProcessSamples(buf, -5, Sr, 16, 1)));
    }

    [Fact]
    public void ノイズだけの区間からはフレームを出さない()
    {
        // 無信号（白色ノイズ）10秒からガベージフレームが出ないこと（83d1f05の対策が維持されている）
        var rnd = new Random(2);
        int len = Sr * 10;
        var noise = new float[len];
        for (int i = 0; i < len; i++) noise[i] = (float)((rnd.NextDouble() * 2 - 1) * 0.8);
        Assert.Equal(0, Decode(noise, len));
    }
}
