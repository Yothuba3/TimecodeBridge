using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
namespace TimecodeBridge.Tests.Services;

using TimecodeBridge.Core.Models;
using TimecodeBridge.Services;

public class TimecodeGeneratorTests : IDisposable
{
    private readonly TimecodeGenerator _generator;

    public TimecodeGeneratorTests()
    {
        _generator = new TimecodeGenerator();
    }

    public void Dispose()
    {
        _generator.Dispose();
    }

    [Fact]
    public void InitialState_IsNotRunning()
    {
        Assert.False(_generator.IsRunning);
    }

    [Theory]
    [InlineData(FrameRate.Fps24, 24)]
    [InlineData(FrameRate.Fps25, 25)]
    [InlineData(FrameRate.Fps2997Drop, 30)]
    [InlineData(FrameRate.Fps30, 30)]
    public async Task CountUp_GeneratesCorrectFrameCount_Per1Second(FrameRate frameRate, int expectedFps)
    {
        var frames = new List<TimecodeValue>();
        var tcs = new TaskCompletionSource();

        _generator.FrameGenerated += (_, tc) =>
        {
            frames.Add(tc);
            if (frames.Count >= expectedFps)
                tcs.TrySetResult();
        };

        var startTime = new TimecodeValue(0, 0, 0, 0, frameRate);
        _generator.Start(startTime, frameRate);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        Assert.True(completed == tcs.Task, $"Expected {expectedFps} frames within 3s, got {frames.Count}");

        Assert.True(frames.Count >= expectedFps);
    }

    [Fact]
    public void Start_SetsIsRunning()
    {
        var startTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30);
        _generator.Start(startTime, FrameRate.Fps30);

        // Give a moment for the task to start
        Thread.Sleep(100);
        Assert.True(_generator.IsRunning);
    }

    [Fact]
    public void Stop_SetsIsRunningToFalse()
    {
        var startTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30);
        _generator.Start(startTime, FrameRate.Fps30);
        Thread.Sleep(100);

        _generator.Stop();
        Thread.Sleep(100);
        Assert.False(_generator.IsRunning);
    }

    [Fact]
    public void Reset_RestoresStartTime()
    {
        var startTime = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30);
        _generator.Start(startTime, FrameRate.Fps30);
        Thread.Sleep(200);

        _generator.Reset();

        Assert.Equal(startTime, _generator.CurrentTimecode);
    }

    [Fact]
    public async Task DropFrame_GeneratesCorrectTimecodes()
    {
        // For 29.97fps drop frame, frames 0 and 1 are skipped at each non-10th minute
        var frames = new List<TimecodeValue>();
        var tcs = new TaskCompletionSource();

        _generator.FrameGenerated += (_, tc) =>
        {
            frames.Add(tc);
            if (frames.Count >= 5)
                tcs.TrySetResult();
        };

        var startTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps2997Drop);
        _generator.Start(startTime, FrameRate.Fps2997Drop);

        await Task.WhenAny(tcs.Task, Task.Delay(3000));

        Assert.True(frames.Count >= 5);
        // First few frames should be sequential from 00:00:00:01
        Assert.Equal(0, frames[0].Hours);
        Assert.Equal(0, frames[0].Minutes);
        Assert.Equal(0, frames[0].Seconds);
        Assert.Equal(1, frames[0].Frames);
    }

    [Fact]
    public void Start_WhenAlreadyRunning_DoesNothing()
    {
        var startTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30);
        _generator.Start(startTime, FrameRate.Fps30);
        Thread.Sleep(100);

        // Second start should not throw or change behavior
        _generator.Start(new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30), FrameRate.Fps30);
        Thread.Sleep(100);

        Assert.True(_generator.IsRunning);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var gen = new TimecodeGenerator();
        gen.Start(new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30), FrameRate.Fps30);
        gen.Dispose();
        gen.Dispose(); // Should not throw
    }
}
