using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
namespace TimecodeBridge.Tests.Services;

using TimecodeBridge.Core.Models;
using TimecodeBridge.Services;
using TimecodeReceiveStatus = TimecodeBridge.Core.Models.TimecodeReceiveStatus;

public class TimecodeEngineTests : IDisposable
{
    private readonly TimecodeEngine _engine;

    public TimecodeEngineTests()
    {
        _engine = new TimecodeEngine(FrameRate.Fps24);
    }

    public void Dispose()
    {
        _engine.Dispose();
    }

    [Fact]
    public async Task WriteFrame_RaisesTimecodeUpdatedEvent()
    {
        var tcs = new TaskCompletionSource<TimecodeUpdatedEventArgs>();
        _engine.TimecodeUpdated += (_, e) => tcs.TrySetResult(e);

        var frame = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps24);
        _engine.WriteFrame(frame);

        var result = await WithTimeout(tcs.Task);

        Assert.Equal(frame, result.RawTimecode);
        Assert.Equal(frame, result.OffsetTimecode);
    }

    [Fact]
    public async Task WriteFrame_AppliesOffsetCorrectly()
    {
        var offset = new TimecodeOffset(false, 0, 0, 5, 0, FrameRate.Fps24);
        _engine.Offset = offset;

        var tcs = new TaskCompletionSource<TimecodeUpdatedEventArgs>();
        _engine.TimecodeUpdated += (_, e) => tcs.TrySetResult(e);

        var frame = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps24);
        _engine.WriteFrame(frame);

        var result = await WithTimeout(tcs.Task);

        Assert.Equal(frame, result.RawTimecode);
        var expected = frame.Add(offset);
        Assert.Equal(expected, result.OffsetTimecode);
    }

    [Fact]
    public async Task WriteFrame_AppliesNegativeOffsetCorrectly()
    {
        var offset = new TimecodeOffset(true, 0, 0, 10, 0, FrameRate.Fps24);
        _engine.Offset = offset;

        var tcs = new TaskCompletionSource<TimecodeUpdatedEventArgs>();
        _engine.TimecodeUpdated += (_, e) => tcs.TrySetResult(e);

        var frame = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps24);
        _engine.WriteFrame(frame);

        var result = await WithTimeout(tcs.Task);

        Assert.Equal(frame, result.RawTimecode);
        var expected = frame.Add(offset);
        Assert.Equal(expected, result.OffsetTimecode);
    }

    [Fact]
    public async Task WriteFrame_UpdatesIsReceivingToTrue()
    {
        Assert.False(_engine.IsReceiving);

        var tcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) => tcs.TrySetResult(e);

        var frame = new TimecodeValue(0, 0, 1, 0, FrameRate.Fps24);
        _engine.WriteFrame(frame);

        var result = await WithTimeout(tcs.Task);

        Assert.True(result.IsReceiving);
        Assert.True(_engine.IsReceiving);
    }

    [Fact]
    public async Task SignalLossDetected_After500ms()
    {
        // First, start receiving
        var receivingTcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (e.IsReceiving)
                receivingTcs.TrySetResult(e);
        };

        var frame = new TimecodeValue(0, 0, 1, 0, FrameRate.Fps24);
        _engine.WriteFrame(frame);
        await WithTimeout(receivingTcs.Task);

        // Now wait for signal loss
        var lostTcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (!e.IsReceiving)
                lostTcs.TrySetResult(e);
        };

        // Wait for signal loss (should happen after ~500ms)
        var result = await WithTimeout(lostTcs.Task, 2000);

        Assert.False(result.IsReceiving);
        Assert.False(_engine.IsReceiving);
    }

    [Fact]
    public async Task CurrentRawTimecode_ReturnsLatestValue()
    {
        var tcs = new TaskCompletionSource<TimecodeUpdatedEventArgs>();
        _engine.TimecodeUpdated += (_, e) => tcs.TrySetResult(e);

        var frame = new TimecodeValue(2, 30, 15, 10, FrameRate.Fps24);
        _engine.WriteFrame(frame);

        await WithTimeout(tcs.Task);

        Assert.Equal(frame, _engine.CurrentRawTimecode);
    }

    [Fact]
    public async Task CurrentOffsetTimecode_ReturnsOffsetAppliedValue()
    {
        var offset = new TimecodeOffset(false, 0, 1, 0, 0, FrameRate.Fps24);
        _engine.Offset = offset;

        var tcs = new TaskCompletionSource<TimecodeUpdatedEventArgs>();
        _engine.TimecodeUpdated += (_, e) => tcs.TrySetResult(e);

        var frame = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps24);
        _engine.WriteFrame(frame);

        await WithTimeout(tcs.Task);

        Assert.Equal(frame, _engine.CurrentRawTimecode);
        Assert.Equal(frame.Add(offset), _engine.CurrentOffsetTimecode);
    }

    [Fact]
    public async Task MultipleFrames_LastValueWins()
    {
        var count = 0;
        var tcs = new TaskCompletionSource<TimecodeUpdatedEventArgs>();
        _engine.TimecodeUpdated += (_, e) =>
        {
            if (Interlocked.Increment(ref count) >= 3)
                tcs.TrySetResult(e);
        };

        _engine.WriteFrame(new TimecodeValue(0, 0, 1, 0, FrameRate.Fps24));
        _engine.WriteFrame(new TimecodeValue(0, 0, 2, 0, FrameRate.Fps24));
        _engine.WriteFrame(new TimecodeValue(0, 0, 3, 0, FrameRate.Fps24));

        var result = await WithTimeout(tcs.Task);

        Assert.Equal(new TimecodeValue(0, 0, 3, 0, FrameRate.Fps24), result.RawTimecode);
        Assert.Equal(new TimecodeValue(0, 0, 3, 0, FrameRate.Fps24), _engine.CurrentRawTimecode);
    }

    [Fact]
    public async Task SignalLoss_NotTriggeredWhileReceiving()
    {
        // Start receiving
        var receivingTcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (e.IsReceiving)
                receivingTcs.TrySetResult(e);
        };

        _engine.WriteFrame(new TimecodeValue(0, 0, 1, 0, FrameRate.Fps24));
        await WithTimeout(receivingTcs.Task);

        // Keep sending frames every 200ms for 1 second
        var lostTcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (!e.IsReceiving)
                lostTcs.TrySetResult(e);
        };

        for (int i = 0; i < 5; i++)
        {
            await Task.Delay(200);
            _engine.WriteFrame(new TimecodeValue(0, 0, 1, i, FrameRate.Fps24));
        }

        // Signal loss should not have occurred
        Assert.False(lostTcs.Task.IsCompleted);
        Assert.True(_engine.IsReceiving);
    }

    [Fact]
    public void FrameRate_ReturnsConfiguredValue()
    {
        Assert.Equal(FrameRate.Fps24, _engine.FrameRate);
    }

    [Fact]
    public void Offset_DefaultIsZero()
    {
        var offset = _engine.Offset;
        Assert.Equal(0, offset.TotalFrames());
    }

    [Fact]
    public void Stop_SetsIsReceivingToFalse()
    {
        _engine.Stop();
        Assert.False(_engine.IsReceiving);
    }

    [Fact]
    public async Task Stop_RaisesStatusChangedIfWasReceiving()
    {
        // Start receiving
        var receivingTcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (e.IsReceiving)
                receivingTcs.TrySetResult(e);
        };

        _engine.WriteFrame(new TimecodeValue(0, 0, 1, 0, FrameRate.Fps24));
        await WithTimeout(receivingTcs.Task);

        // Now stop
        var stoppedTcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (!e.IsReceiving)
                stoppedTcs.TrySetResult(e);
        };

        _engine.Stop();

        var result = await WithTimeout(stoppedTcs.Task);
        Assert.False(result.IsReceiving);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var engine = new TimecodeEngine(FrameRate.Fps30);
        engine.WriteFrame(new TimecodeValue(0, 0, 1, 0, FrameRate.Fps30));
        engine.Dispose();
        // Double dispose should also be safe
        engine.Dispose();
    }

    #region Freerun Tests

    [Fact]
    public async Task Freerun_StartsAfterSignalLoss_WhenEnabled()
    {
        _engine.FreerunDurationSeconds = 5;

        // Start receiving
        var receivingTcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (e.Status == TimecodeReceiveStatus.Receiving)
                receivingTcs.TrySetResult(e);
        };

        _engine.WriteFrame(new TimecodeValue(0, 0, 1, 0, FrameRate.Fps24));
        await WithTimeout(receivingTcs.Task);

        // Wait for signal loss → should trigger Freerunning, not NotReceiving
        var freerunTcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (e.Status == TimecodeReceiveStatus.Freerunning)
                freerunTcs.TrySetResult(e);
        };

        var result = await WithTimeout(freerunTcs.Task, 2000);

        Assert.Equal(TimecodeReceiveStatus.Freerunning, result.Status);
        Assert.True(result.IsReceiving); // backward compat: Freerunning counts as "receiving"
        Assert.True(_engine.IsFreerunning);
    }

    [Fact]
    public async Task Freerun_Disabled_GoesDirectlyToNotReceiving()
    {
        _engine.FreerunDurationSeconds = 0; // disabled (default)

        // Start receiving
        var receivingTcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (e.Status == TimecodeReceiveStatus.Receiving)
                receivingTcs.TrySetResult(e);
        };

        _engine.WriteFrame(new TimecodeValue(0, 0, 1, 0, FrameRate.Fps24));
        await WithTimeout(receivingTcs.Task);

        // Wait for signal loss → should go directly to NotReceiving
        var lostTcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (e.Status == TimecodeReceiveStatus.NotReceiving)
                lostTcs.TrySetResult(e);
        };

        var result = await WithTimeout(lostTcs.Task, 2000);

        Assert.Equal(TimecodeReceiveStatus.NotReceiving, result.Status);
        Assert.False(_engine.IsFreerunning);
    }

    [Fact]
    public async Task Freerun_ContinuesTimecodeUpdated()
    {
        _engine.FreerunDurationSeconds = 5;

        // Start receiving
        var receivingTcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (e.Status == TimecodeReceiveStatus.Receiving)
                receivingTcs.TrySetResult(e);
        };

        _engine.WriteFrame(new TimecodeValue(0, 0, 10, 0, FrameRate.Fps24));
        await WithTimeout(receivingTcs.Task);

        // Wait for freerun to start
        var freerunTcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (e.Status == TimecodeReceiveStatus.Freerunning)
                freerunTcs.TrySetResult(e);
        };
        await WithTimeout(freerunTcs.Task, 2000);

        // Collect some freerun frames
        var frames = new List<TimecodeValue>();
        var frameCountTcs = new TaskCompletionSource();
        _engine.TimecodeUpdated += (_, e) =>
        {
            frames.Add(e.RawTimecode);
            if (frames.Count >= 3)
                frameCountTcs.TrySetResult();
        };

        await WithTimeout(Task.WhenAny(frameCountTcs.Task, Task.Delay(2000)));

        Assert.True(frames.Count >= 3, $"Expected at least 3 freerun frames, got {frames.Count}");

        // Verify frames are incrementing from the last received frame
        var baseTotalFrames = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps24).TotalFrames();
        foreach (var frame in frames)
        {
            Assert.True(frame.TotalFrames() > baseTotalFrames,
                $"Freerun frame {frame} should be after base timecode");
        }
    }

    [Fact]
    public async Task Freerun_ExpiresAfterDuration()
    {
        _engine.FreerunDurationSeconds = 0.5; // 500ms freerun

        // Start receiving
        var receivingTcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (e.Status == TimecodeReceiveStatus.Receiving)
                receivingTcs.TrySetResult(e);
        };

        _engine.WriteFrame(new TimecodeValue(0, 0, 1, 0, FrameRate.Fps24));
        await WithTimeout(receivingTcs.Task);

        // Wait for freerun start
        var freerunTcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (e.Status == TimecodeReceiveStatus.Freerunning)
                freerunTcs.TrySetResult(e);
        };
        await WithTimeout(freerunTcs.Task, 2000);

        // Wait for expiry → NotReceiving
        var expiredTcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (e.Status == TimecodeReceiveStatus.NotReceiving)
                expiredTcs.TrySetResult(e);
        };

        var result = await WithTimeout(expiredTcs.Task, 3000);

        Assert.Equal(TimecodeReceiveStatus.NotReceiving, result.Status);
        Assert.False(_engine.IsFreerunning);
    }

    [Fact]
    public async Task Freerun_LtcReturns_TransitionsBackToReceiving()
    {
        _engine.FreerunDurationSeconds = 5;

        // Start receiving
        var receivingTcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (e.Status == TimecodeReceiveStatus.Receiving)
                receivingTcs.TrySetResult(e);
        };

        _engine.WriteFrame(new TimecodeValue(0, 0, 1, 0, FrameRate.Fps24));
        await WithTimeout(receivingTcs.Task);

        // Wait for freerun
        var freerunTcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (e.Status == TimecodeReceiveStatus.Freerunning)
                freerunTcs.TrySetResult(e);
        };
        await WithTimeout(freerunTcs.Task, 2000);
        Assert.True(_engine.IsFreerunning);

        // Send a real frame → should go back to Receiving
        var reReceivingTcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (e.Status == TimecodeReceiveStatus.Receiving)
                reReceivingTcs.TrySetResult(e);
        };

        _engine.WriteFrame(new TimecodeValue(0, 0, 2, 0, FrameRate.Fps24));

        var result = await WithTimeout(reReceivingTcs.Task, 2000);

        Assert.Equal(TimecodeReceiveStatus.Receiving, result.Status);
        Assert.False(_engine.IsFreerunning);
    }

    [Fact]
    public async Task Freerun_FrameContinuity_IncrementsFromLastTimecode()
    {
        _engine.FreerunDurationSeconds = 5;

        var lastRealFrame = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps24);

        // Start receiving
        var receivingTcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (e.Status == TimecodeReceiveStatus.Receiving)
                receivingTcs.TrySetResult(e);
        };

        _engine.WriteFrame(lastRealFrame);
        await WithTimeout(receivingTcs.Task);

        // Wait for freerun
        var freerunTcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (e.Status == TimecodeReceiveStatus.Freerunning)
                freerunTcs.TrySetResult(e);
        };
        await WithTimeout(freerunTcs.Task, 2000);

        // Capture the first freerun frame
        var firstFreerunTcs = new TaskCompletionSource<TimecodeUpdatedEventArgs>();
        _engine.TimecodeUpdated += (_, e) => firstFreerunTcs.TrySetResult(e);

        var firstFreerun = await WithTimeout(firstFreerunTcs.Task, 2000);

        // First freerun frame should be lastRealFrame + 1
        var expectedFirstFrame = TimecodeValue.FromTotalFrames(lastRealFrame.TotalFrames() + 1, FrameRate.Fps24);
        Assert.Equal(expectedFirstFrame, firstFreerun.RawTimecode);
    }

    #endregion

    private static async Task<T> WithTimeout<T>(Task<T> task, int timeoutMs = 3000)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
        if (completed != task)
            throw new TimeoutException($"Operation did not complete within {timeoutMs}ms.");
        return await task;
    }
}
