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
