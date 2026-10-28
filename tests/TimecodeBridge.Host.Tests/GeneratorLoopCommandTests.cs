using TimecodeBridge.Core.Models;
using TimecodeBridge.Host.Bridge;
using Xunit;

namespace TimecodeBridge.Host.Tests;

/// <summary>
/// 内部生成の範囲ループを generator.configure で設定する。
/// 開始 TC より後でなければ範囲にならないので受け付けない。
/// </summary>
public class GeneratorLoopCommandTests
{
    private static HostHarness Started()
    {
        var h = new HostHarness();
        h.Run("generator.configure", """{"startTime":"08:00:00:00","frameRate":"30"}""");
        return h;
    }

    [Fact]
    public void 開始より後の終了TCを受け付ける()
    {
        var h = Started();

        Assert.True(h.Run("generator.configure", """{"loopEndTime":"08:00:30:00"}""").Ok);

        Assert.Equal("08:00:30:00", h.State.Generator.LoopEndTime!.Value.ToString());
    }

    [Fact]
    public void 空文字でループを解除する()
    {
        var h = Started();
        h.Run("generator.configure", """{"loopEndTime":"08:00:30:00"}""");

        Assert.True(h.Run("generator.configure", """{"loopEndTime":""}""").Ok);

        Assert.Null(h.State.Generator.LoopEndTime);
    }

    [Theory]
    [InlineData("08:00:00:00")]
    [InlineData("07:59:59:29")]
    public void 開始以前の終了TCは受け付けない(string loopEnd)
    {
        var h = Started();

        var result = h.Run("generator.configure", $$"""{"loopEndTime":"{{loopEnd}}"}""");

        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
        Assert.Null(h.State.Generator.LoopEndTime);
    }

    [Fact]
    public void 形式が違う終了TCは受け付けない()
    {
        var h = Started();

        var result = h.Run("generator.configure", """{"loopEndTime":"25:00:00:00"}""");

        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
    }

    [Fact]
    public void 開始TCを終了より後へ動かすとループは外れる()
    {
        var h = Started();
        h.Run("generator.configure", """{"loopEndTime":"08:00:30:00"}""");

        h.Run("generator.configure", """{"startTime":"08:01:00:00"}""");

        Assert.Null(h.State.Generator.LoopEndTime);
    }

    // Web は入力のたびに開始 TC と終了 TC を両方送る。弾いたのに開始 TC だけ書き換わると、終了が開始より前のループが残っていた
    [Fact]
    public void 開始TCが終了TC以降になる組み合わせは弾いて何も変えない()
    {
        var h = new HostHarness();
        h.Run("generator.configure", """{"startTime":"01:00:00:00","loopEndTime":"01:03:10:00","frameRate":"30"}""");

        var result = h.Run("generator.configure", """{"startTime":"03:45:11:20","loopEndTime":"01:03:10:00","frameRate":"30"}""");

        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
        Assert.Equal("01:00:00:00", h.State.Generator.StartTime.ToString());
        Assert.Equal("01:03:10:00", h.State.Generator.LoopEndTime!.Value.ToString());
    }

    [Fact]
    public void 新しいレートにない開始TCはレートごと弾く()
    {
        var h = Started();
        h.Run("generator.configure", """{"startTime":"08:00:00:29"}""");

        var result = h.Run("generator.configure", """{"startTime":"08:00:00:29","frameRate":"24"}""");

        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
        Assert.Equal(FrameRate.Fps30, h.State.Generator.FrameRate);
        Assert.Equal("08:00:00:29", h.State.Generator.StartTime.ToString());
    }

    [Fact]
    public void 音量が範囲外なら一緒に送った開始TCとループも変えない()
    {
        var h = Started();

        var result = h.Run("generator.configure", """{"startTime":"09:00:00:00","loopEndTime":"09:00:30:00","volume":2}""");

        Assert.Equal(ErrorCode.Validation, result.Error!.Code);
        Assert.Equal("08:00:00:00", h.State.Generator.StartTime.ToString());
        Assert.Null(h.State.Generator.LoopEndTime);
    }

    [Fact]
    public void フレームレートを下げると終了TCのフレーム番号を丸める()
    {
        var h = Started();
        h.Run("generator.configure", """{"loopEndTime":"08:00:30:29"}""");

        h.Run("generator.configure", """{"frameRate":"24"}""");

        Assert.Equal("08:00:30:23", h.State.Generator.LoopEndTime!.Value.ToString());
    }

    [Fact]
    public void ループは画面の状態に乗る()
    {
        var h = Started();
        h.Run("generator.configure", """{"loopEndTime":"08:00:30:00"}""");

        var generator = h.State.BuildSnapshot().Generator;

        Assert.Equal("08:00:30:00", generator.LoopEndTime);
    }

    [Fact]
    public void 一時停止中にループを変えたら続きからではなく掛け直す()
    {
        var h = Started();
        h.Run("generator.configure", """{"loopEndTime":"08:00:05:00"}""");
        h.Run("generator.start");
        h.Engine.ActiveSource = TimecodeSourceType.Generator;
        h.Engine.CurrentRawTimecode = new TimecodeValue(8, 0, 0, 3, FrameRate.Fps30);
        h.Run("generator.stop");
        h.Engine.Calls.Clear();

        h.Run("generator.configure", """{"loopEndTime":"08:00:30:00"}""");
        h.Run("generator.start");

        // 続きから再開すると、変えた範囲が効かないまま進んでしまう
        Assert.DoesNotContain("ResumeGenerator", h.Engine.Calls);
        Assert.Contains("StartGenerator:08:00:00:00", h.Engine.Calls);
    }

    [Fact]
    public void 設定を変えていなければ一時停止から続きを再開する()
    {
        var h = Started();
        h.Run("generator.start");
        h.Engine.ActiveSource = TimecodeSourceType.Generator;
        h.Engine.CurrentRawTimecode = new TimecodeValue(8, 0, 0, 3, FrameRate.Fps30);
        h.Run("generator.stop");
        h.Engine.Calls.Clear();

        h.Run("generator.start");

        Assert.Contains("ResumeGenerator", h.Engine.Calls);
    }

    [Fact]
    public void ループなしのときは状態に乗らない()
    {
        var h = Started();

        Assert.Null(h.State.BuildSnapshot().Generator.LoopEndTime);
    }
}
