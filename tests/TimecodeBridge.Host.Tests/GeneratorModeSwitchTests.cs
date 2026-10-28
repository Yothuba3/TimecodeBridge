using Avalonia.Headless.XUnit;
using TimecodeBridge.Core.Audio;
using TimecodeBridge.Core.Models;
using Xunit;

namespace TimecodeBridge.Host.Tests;

/// <summary>
/// 内部生成 → 再生 →(停止)→ LTC受信 → 内部生成 → 再生 で時計が動き出すこと。
/// mode.set は生成器を破棄するので、戻ってきたあとの再生は「続きから」ではなく生成し直しになる。
/// (以前はエンジンが ActiveSource を Generator のまま残し、破棄済みの生成器の再開に入って
/// result は ok のまま時計が止まり、フッターも UNLOCK のままだった)
/// </summary>
public class GeneratorModeSwitchTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void LTC受信を挟んで戻ったあとの再生は生成し直す(bool pauseBeforeSwitch)
    {
        var h = new HostHarness();
        h.Run("mode.set", """{"mode":"generate"}""");
        h.Run("generator.start");
        h.Engine.CurrentRawTimecode = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30);
        if (pauseBeforeSwitch) h.Run("generator.stop");
        h.Run("mode.set", """{"mode":"ltc"}"""); // 入力デバイス未選択なので LTC 受信は始まらない
        h.Run("mode.set", """{"mode":"generate"}""");
        h.Engine.Calls.Clear();

        Assert.True(h.Run("generator.start").Ok);

        Assert.DoesNotContain("ResumeGenerator", h.Engine.Calls);
        Assert.Contains("StartGenerator:00:00:00:00", h.Engine.Calls);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void 本物のエンジンでもLTC受信を挟んで戻ったあとの再生で時計が進む(bool pauseBeforeSwitch)
    {
        using var engine = HostHarness.RealEngine();
        var h = new HostHarness(engine);
        h.Run("mode.set", """{"mode":"generate"}""");
        Assert.True(h.Run("generator.start").Ok);
        AssertClockAdvances(h, engine);
        if (pauseBeforeSwitch) h.Run("generator.stop");
        h.Run("mode.set", """{"mode":"ltc"}""");
        h.Run("mode.set", """{"mode":"generate"}""");

        Assert.True(h.Run("generator.start").Ok);

        AssertClockAdvances(h, engine);
    }

    [AvaloniaFact]
    public void 本物のエンジンで一時停止からの再生は止めた位置の続きから進む()
    {
        using var engine = HostHarness.RealEngine();
        var h = new HostHarness(engine);
        h.Run("mode.set", """{"mode":"generate"}""");
        h.Run("generator.start");
        Assert.True(SpinWait.SpinUntil(() => engine.CurrentRawTimecode.ToOrdinal() >= 15, TimeSpan.FromSeconds(3)));
        h.Run("generator.stop");
        Thread.Sleep(100); // 止める直前に積まれたフレームが行き渡ってから位置を読む
        var paused = engine.CurrentRawTimecode.ToOrdinal();

        Assert.True(h.Run("generator.start").Ok);

        Assert.True(SpinWait.SpinUntil(() => engine.CurrentRawTimecode.ToOrdinal() != paused, TimeSpan.FromSeconds(2)), "再開しても時計が進まない");
        Assert.True(engine.CurrentRawTimecode.ToOrdinal() > paused, "続きからではなく開始 TC からやり直した");
    }

    // 時計が進んでいる = 受信中(フッターの LOCK)で、TC が先へ進んでいく
    private static void AssertClockAdvances(HostHarness h, TimecodeEngine engine)
    {
        Assert.True(SpinWait.SpinUntil(() => engine.IsReceiving, TimeSpan.FromSeconds(2)), "生成が始まらない(UNLOCK のまま)");
        var first = engine.CurrentRawTimecode.ToOrdinal();
        Assert.True(SpinWait.SpinUntil(() => engine.CurrentRawTimecode.ToOrdinal() > first, TimeSpan.FromSeconds(2)), "時計が進まない");
        Assert.True(h.State.BuildSnapshot().Transport.Locked);
    }
}
