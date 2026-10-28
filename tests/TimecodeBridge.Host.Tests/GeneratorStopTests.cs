using Avalonia.Headless.XUnit;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using Xunit;

namespace TimecodeBridge.Host.Tests;

/// <summary>
/// 内部生成を止めたら本当に止まること。
/// 以前は生成器が「動いている」印をタイマーの Task の中で立てていたので、開始の直後に来た Stop が素通りし、
/// 止めたまま FPS を変えてリセットすると(Host は作り直して開始 → すぐ停止する)、
/// 画面は「停止」のまま LTC が出続け、受け手のキューまで発火した。
/// </summary>
public class GeneratorStopTests
{
    private static readonly TimecodeValue Start = new(1, 0, 0, 0, FrameRate.Fps30);

    private static (TimecodeGenerator Generator, Func<int> Count) Counted()
    {
        var generator = new TimecodeGenerator();
        var count = 0;
        generator.FrameGenerated += (_, _) => Interlocked.Increment(ref count);
        return (generator, () => Volatile.Read(ref count));
    }

    [Fact]
    public void 開始した直後に止めればフレームを出さない()
    {
        // 競合なので 1 回では外れることがある。何度か繰り返す
        for (var i = 0; i < 10; i++)
        {
            var (generator, count) = Counted();
            using (generator)
            {
                generator.Start(Start, FrameRate.Fps30);
                generator.Stop();

                Thread.Sleep(150); // 30fps なら 4 フレームぶん
                Assert.False(generator.IsRunning, $"{i + 1} 回目: 止めたのに動いている");
                Assert.Equal(0, count());
            }
        }
    }

    [Fact]
    public void 動かしたままリセットした直後に止めても止まる()
    {
        for (var i = 0; i < 5; i++)
        {
            var (generator, count) = Counted();
            using (generator)
            {
                generator.Start(Start, FrameRate.Fps30);
                Assert.True(SpinWait.SpinUntil(() => count() >= 3, TimeSpan.FromSeconds(2)));

                generator.ResetTo(Start); // 動いていればループを張り直す
                generator.Stop();
                Thread.Sleep(50); // 止める前に積まれたぶんが出終わるのを待つ
                var stopped = count();

                Thread.Sleep(200);
                Assert.False(generator.IsRunning, $"{i + 1} 回目: 止めたのに動いている");
                Assert.Equal(stopped, count());
            }
        }
    }

    [Fact]
    public void 止めてすぐ再開したループは前のループが終わっても止められる()
    {
        var (generator, count) = Counted();
        using (generator)
        {
            generator.Start(Start, FrameRate.Fps30);
            Assert.True(SpinWait.SpinUntil(() => count() >= 3, TimeSpan.FromSeconds(2)));

            generator.Stop();
            generator.Resume();
            Thread.Sleep(100); // 前のループが後始末を終える
            Assert.True(generator.IsRunning, "再開したのに止まっている扱いになった");

            generator.Stop();
            Thread.Sleep(50);
            var stopped = count();
            Thread.Sleep(200);
            Assert.Equal(stopped, count());
        }
    }

    [AvaloniaFact]
    public void 止めたままFPSを変えてリセットすると開始TCで止まったままになる()
    {
        using var engine = HostHarness.RealEngine();
        var h = new HostHarness(engine);
        h.Run("mode.set", """{"mode":"generate"}""");
        h.Run("generator.configure", """{"startTime":"01:00:00:00","frameRate":"30"}""");
        h.Run("generator.start");
        Assert.True(SpinWait.SpinUntil(() => engine.CurrentRawTimecode.ToOrdinal() >= 108_010, TimeSpan.FromSeconds(3)));
        h.Run("generator.stop");

        h.Run("generator.configure", """{"frameRate":"25"}""");
        Assert.True(h.Run("generator.reset").Ok);

        Assert.True(SpinWait.SpinUntil(() => engine.CurrentRawTimecode.ToString() == "01:00:00:00", TimeSpan.FromSeconds(1)), $"開始 TC に戻らない: {engine.CurrentRawTimecode}");
        Thread.Sleep(300);
        Assert.Equal("01:00:00:00", engine.CurrentRawTimecode.ToString()); // 止まったまま進まない
        Assert.Equal(FrameRate.Fps25, engine.FrameRate);
        Assert.False(h.State.GeneratorRunning);

        // 再生すれば開始 TC から進む(裏で進んでいた位置からではない)
        Assert.True(h.Run("generator.start").Ok);
        Assert.True(SpinWait.SpinUntil(() => engine.CurrentRawTimecode.ToOrdinal() > 108_000, TimeSpan.FromSeconds(2)), "再生しても時計が進まない");
        Assert.StartsWith("01:00:00:", engine.CurrentRawTimecode.ToString()); // 開始 TC の 1 秒目のうち
    }
}
