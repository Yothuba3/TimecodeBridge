using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Services;

namespace TimecodeBridge.Tests.Services;

public class FreerunControllerTests : IDisposable
{
    private readonly FreerunController _controller;

    public FreerunControllerTests()
    {
        _controller = new FreerunController();
    }

    public void Dispose()
    {
        _controller.Dispose();
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        _controller.Dispose();
        _controller.Dispose(); // double-dispose safety
    }

    [Fact]
    public void Stop_BeforeStart_DoesNotThrow()
    {
        _controller.Stop();
    }

    [Fact]
    public void IsFreerunning_DefaultsToFalse()
    {
        Assert.False(_controller.IsFreerunning);
    }

    [Fact]
    public void OnFrameGenerated_DefaultsToNull()
    {
        Assert.Null(_controller.OnFrameGenerated);
    }

    [Fact]
    public void Start_SetsIsFreerunningToTrue()
    {
        var lastFrame = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30);
        var offset = TimecodeOffset.Zero(FrameRate.Fps30);

        _controller.Start(lastFrame, offset, FrameRate.Fps30, durationSeconds: 5.0);

        Assert.True(_controller.IsFreerunning);
    }

    [Fact]
    public void Start_InvokesOnFrameGenerated()
    {
        var frames = new List<(TimecodeValue raw, TimecodeValue offset)>();
        _controller.OnFrameGenerated = (raw, offset) => frames.Add((raw, offset));

        var lastFrame = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30);
        var zeroOffset = TimecodeOffset.Zero(FrameRate.Fps30);

        _controller.Start(lastFrame, zeroOffset, FrameRate.Fps30, durationSeconds: 5.0);

        Thread.Sleep(300);

        _controller.Stop();

        Assert.NotEmpty(frames);
        // Frames should increment from lastFrame
        Assert.True(frames[0].raw.TotalFrames() > lastFrame.TotalFrames());
    }

    [Fact]
    public void Stop_SetsIsFreerunningToFalse()
    {
        var lastFrame = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30);
        var offset = TimecodeOffset.Zero(FrameRate.Fps30);

        _controller.Start(lastFrame, offset, FrameRate.Fps30, durationSeconds: 5.0);
        Assert.True(_controller.IsFreerunning);

        _controller.Stop();
        Assert.False(_controller.IsFreerunning);
    }

    [Fact]
    public void Start_AppliesOffset()
    {
        var frames = new List<(TimecodeValue raw, TimecodeValue offset)>();
        _controller.OnFrameGenerated = (raw, offset) => frames.Add((raw, offset));

        var lastFrame = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30);
        // Create an offset of +1 hour
        var offset = new TimecodeOffset(false, 1, 0, 0, 0, FrameRate.Fps30);

        _controller.Start(lastFrame, offset, FrameRate.Fps30, durationSeconds: 5.0);

        Thread.Sleep(200);

        _controller.Stop();

        Assert.NotEmpty(frames);
        // Offset frame should be ~2 hours (1h raw + 1h offset)
        Assert.True(frames[0].offset.Hours >= 2);
    }

    [Fact]
    public void Start_AutoStopsAfterDuration()
    {
        var onExpired = new ManualResetEventSlim(false);
        _controller.OnExpired = () =>
        {
            // Caller is responsible for stopping after expiry notification
            _controller.Stop();
            onExpired.Set();
        };

        var lastFrame = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30);
        var offset = TimecodeOffset.Zero(FrameRate.Fps30);

        _controller.Start(lastFrame, offset, FrameRate.Fps30, durationSeconds: 0.3);

        bool expired = onExpired.Wait(2000);
        Assert.True(expired);
        Assert.False(_controller.IsFreerunning);
    }

    [Fact]
    public void Dispose_WhileRunning_StopsCleanly()
    {
        var lastFrame = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30);
        var offset = TimecodeOffset.Zero(FrameRate.Fps30);

        _controller.Start(lastFrame, offset, FrameRate.Fps30, durationSeconds: 10.0);
        Assert.True(_controller.IsFreerunning);

        // Should not throw or hang
        _controller.Dispose();
        Assert.False(_controller.IsFreerunning);
    }
}
