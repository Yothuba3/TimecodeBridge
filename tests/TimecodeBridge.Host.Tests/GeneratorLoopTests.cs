using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using Xunit;

namespace TimecodeBridge.Host.Tests;

/// <summary>
/// 内部生成の範囲ループ。08:00:00:00〜08:00:30:00 と指定したら
/// その 30 秒を繰り返す。実時間を待たずに済むよう、ごく短い範囲で
/// 周回の境界を確かめる。
/// </summary>
public class GeneratorLoopTests
{
    private static List<TimecodeValue> Collect(TimecodeValue start, TimecodeValue? end, FrameRate rate, int count)
    {
        var frames = new List<TimecodeValue>();
        using var generator = new TimecodeGenerator();
        var done = new TaskCompletionSource();
        generator.FrameGenerated += (_, tc) =>
        {
            if (frames.Count >= count) return;
            frames.Add(tc);
            if (frames.Count >= count) done.TrySetResult();
        };
        generator.Start(start, rate, end);
        Task.WhenAny(done.Task, Task.Delay(5000)).GetAwaiter().GetResult();
        generator.Stop();
        return frames;
    }

    [Fact]
    public void 範囲の終端を鳴らしてから開始位置に戻る()
    {
        var start = new TimecodeValue(8, 0, 0, 0, FrameRate.Fps30);
        var end = new TimecodeValue(8, 0, 0, 4, FrameRate.Fps30);

        // 1 周 5 フレーム。2 周ぶん見る
        var frames = Collect(start, end, FrameRate.Fps30, 10);

        Assert.Equal(10, frames.Count);
        Assert.Equal(
            ["08:00:00:01", "08:00:00:02", "08:00:00:03", "08:00:00:04",
             "08:00:00:00", "08:00:00:01", "08:00:00:02", "08:00:00:03", "08:00:00:04",
             "08:00:00:00"],
            frames.Select(f => f.ToString()));
    }

    [Fact]
    public void 終端を与えなければ範囲を越えて進み続ける()
    {
        var start = new TimecodeValue(8, 0, 0, 0, FrameRate.Fps30);

        var frames = Collect(start, null, FrameRate.Fps30, 6);

        Assert.Equal(
            ["08:00:00:01", "08:00:00:02", "08:00:00:03", "08:00:00:04", "08:00:00:05", "08:00:00:06"],
            frames.Select(f => f.ToString()));
    }

    [Theory]
    [InlineData(FrameRate.Fps24)]
    [InlineData(FrameRate.Fps25)]
    [InlineData(FrameRate.Fps2997Drop)]
    [InlineData(FrameRate.Fps30)]
    public void どのフレームレートでも境界は同じ位置に来る(FrameRate rate)
    {
        var start = new TimecodeValue(8, 0, 0, 0, rate);
        var end = new TimecodeValue(8, 0, 0, 2, rate);

        // 1 周 3 フレーム。3 周ぶん
        var frames = Collect(start, end, rate, 9);

        Assert.Equal(9, frames.Count);
        Assert.Equal(
            [1, 2, 0, 1, 2, 0, 1, 2, 0],
            frames.Select(f => f.Frames));
    }

    [Fact]
    public void 開始より手前の終端はループとして扱わない()
    {
        var start = new TimecodeValue(8, 0, 0, 10, FrameRate.Fps30);
        var end = new TimecodeValue(8, 0, 0, 5, FrameRate.Fps30);

        var frames = Collect(start, end, FrameRate.Fps30, 3);

        Assert.Equal(
            ["08:00:00:11", "08:00:00:12", "08:00:00:13"],
            frames.Select(f => f.ToString()));
    }

    [Fact]
    public void 開始と同じ終端はループとして扱わない()
    {
        var start = new TimecodeValue(8, 0, 0, 0, FrameRate.Fps30);

        var frames = Collect(start, start, FrameRate.Fps30, 3);

        Assert.Equal(
            ["08:00:00:01", "08:00:00:02", "08:00:00:03"],
            frames.Select(f => f.ToString()));
    }

    [Fact]
    public void 別の位置へ頭出しするとループ範囲は外れる()
    {
        var start = new TimecodeValue(8, 0, 0, 0, FrameRate.Fps30);
        var frames = new List<TimecodeValue>();
        using var generator = new TimecodeGenerator();

        generator.Start(start, FrameRate.Fps30, new TimecodeValue(8, 0, 0, 2, FrameRate.Fps30));
        generator.Stop();

        var done = new TaskCompletionSource();
        generator.FrameGenerated += (_, tc) =>
        {
            if (frames.Count >= 5) return;
            frames.Add(tc);
            if (frames.Count >= 5) done.TrySetResult();
        };
        generator.ResetTo(new TimecodeValue(9, 0, 0, 0, FrameRate.Fps30));
        generator.Resume();
        Task.WhenAny(done.Task, Task.Delay(5000)).GetAwaiter().GetResult();
        generator.Stop();

        // 先頭は ResetTo が通知する頭出し位置そのもの
        Assert.Equal("09:00:00:00", frames[0].ToString());
        Assert.DoesNotContain("09:00:00:00", frames.Skip(1).Select(f => f.ToString()));
    }
}
