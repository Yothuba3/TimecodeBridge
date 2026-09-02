using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;

namespace TimecodeBridge.App.Tests.Services;

public class LtcFrameRateDetectorTests
{
    /// <summary>
    /// startSecond から seconds 秒ぶん、デコーダが付けるのと同じ形（DFフラグ以外は番号で推定したレート）で
    /// フレームを流し、最後に返ったレートを返す。
    /// </summary>
    private static FrameRate Feed(LtcFrameRateDetector detector, int fps, bool dropFrame, int startSecond, int seconds, int skipFrame = -1)
    {
        FrameRate last = detector.Confirmed;
        for (int s = startSecond; s < startSecond + seconds; s++)
        {
            for (int f = 0; f < fps; f++)
            {
                if (f == skipFrame) continue;
                var frame = new TimecodeValue(0, s / 60, s % 60, f, LtcDecoder.DetermineFrameRate(f, dropFrame));
                last = detector.Observe(frame);
            }
        }
        return last;
    }

    [Theory]
    [InlineData(24, false, FrameRate.Fps24)]
    [InlineData(25, false, FrameRate.Fps25)]
    [InlineData(30, false, FrameRate.Fps30)]
    [InlineData(30, true, FrameRate.Fps2997Drop)]
    public void 初期値に関係なく信号のレートを確定する(int fps, bool dropFrame, FrameRate expected)
    {
        foreach (var initial in new[] { FrameRate.Fps24, FrameRate.Fps30 })
        {
            var detector = new LtcFrameRateDetector(initial);

            // 秒の途中から受信開始（フレーム5から）。完全な秒が2つ揃った次のフレームで確定する
            var frame = new TimecodeValue(0, 0, 10, 5, LtcDecoder.DetermineFrameRate(5, dropFrame));
            detector.Observe(frame);
            for (int f = 6; f < fps; f++) detector.Observe(new TimecodeValue(0, 0, 10, f, LtcDecoder.DetermineFrameRate(f, dropFrame)));
            Feed(detector, fps, dropFrame, 11, 2);

            var afterTwoCompleteSeconds = Feed(detector, fps, dropFrame, 13, 1);
            Assert.Equal(expected, afterTwoCompleteSeconds);
            Assert.Equal(expected, detector.Confirmed);
        }
    }

    [Fact]
    public void 途中で30fpsから24fpsの信号源に変わると追従する()
    {
        var detector = new LtcFrameRateDetector(FrameRate.Fps30);
        Feed(detector, 30, false, 0, 4);
        Assert.Equal(FrameRate.Fps30, detector.Confirmed);

        // 別の信号源（タイムコードが飛ぶ）。ジャンプ後は古い証拠を捨て、完全な秒2つで確定し直す
        Feed(detector, 24, false, 100, 1);
        Assert.Equal(FrameRate.Fps30, detector.Confirmed);
        Feed(detector, 24, false, 101, 2);
        var last = Feed(detector, 24, false, 103, 1);

        Assert.Equal(FrameRate.Fps24, last);
        Assert.Equal(FrameRate.Fps24, detector.Confirmed);
    }

    [Fact]
    public void 途中で24fpsから30fpsに変わると番号が出た時点で引き上げ後に確定する()
    {
        var detector = new LtcFrameRateDetector(FrameRate.Fps24);
        Feed(detector, 24, false, 0, 4);
        Assert.Equal(FrameRate.Fps24, detector.Confirmed);

        // 24fps扱いのままでは表せないフレーム番号はその場で引き上げる
        var bumped = detector.Observe(new TimecodeValue(0, 1, 40, 27, FrameRate.Fps30));
        Assert.Equal(FrameRate.Fps30, bumped);
        Assert.Equal(FrameRate.Fps24, detector.Confirmed);

        Feed(detector, 30, false, 101, 3);
        Assert.Equal(FrameRate.Fps30, detector.Confirmed);
    }

    [Fact]
    public void 秒境界の最大フレームが1回欠けただけでは25fpsを24fpsに落とさない()
    {
        var detector = new LtcFrameRateDetector(FrameRate.Fps30);
        Feed(detector, 25, false, 0, 4);
        Assert.Equal(FrameRate.Fps25, detector.Confirmed);

        Feed(detector, 25, false, 4, 1, skipFrame: 24);
        var last = Feed(detector, 25, false, 5, 2);

        Assert.Equal(FrameRate.Fps25, last);
    }

    [Fact]
    public void 半分以上欠けた秒は証拠にしない()
    {
        var detector = new LtcFrameRateDetector(FrameRate.Fps30);
        Feed(detector, 30, false, 0, 3);
        Assert.Equal(FrameRate.Fps30, detector.Confirmed);

        // 秒3はフレーム0〜9だけ、秒4はフレーム0〜4だけ受信（連続した秒だが不完全）。
        // これを証拠に採ると最大フレーム9で24fpsに落ちてしまう
        for (int f = 0; f < 10; f++) detector.Observe(new TimecodeValue(0, 0, 3, f, FrameRate.Fps24));
        for (int f = 0; f < 5; f++) detector.Observe(new TimecodeValue(0, 0, 4, f, FrameRate.Fps24));
        var last = detector.Observe(new TimecodeValue(0, 0, 5, 0, FrameRate.Fps24));

        Assert.Equal(FrameRate.Fps30, last);
    }

    [Fact]
    public void DFフラグが消えたら非DFへ戻る()
    {
        var detector = new LtcFrameRateDetector(FrameRate.Fps30);
        Feed(detector, 30, true, 0, 4);
        Assert.Equal(FrameRate.Fps2997Drop, detector.Confirmed);

        Feed(detector, 30, false, 4, 3);

        Assert.Equal(FrameRate.Fps30, detector.Confirmed);
    }

    [Fact]
    public void Resetで証拠と確定値を初期化する()
    {
        var detector = new LtcFrameRateDetector(FrameRate.Fps30);
        Feed(detector, 24, false, 0, 4);
        Assert.Equal(FrameRate.Fps24, detector.Confirmed);

        detector.Reset(FrameRate.Fps25);

        Assert.Equal(FrameRate.Fps25, detector.Confirmed);
        Assert.Equal(FrameRate.Fps25, Feed(detector, 24, false, 10, 1));
    }
}
