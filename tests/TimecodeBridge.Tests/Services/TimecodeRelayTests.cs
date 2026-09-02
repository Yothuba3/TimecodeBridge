namespace TimecodeBridge.Tests.Services;

using TimecodeBridge.Models;
using TimecodeBridge.Services;
using TimecodeBridge.Services.Interfaces;
using TimecodeReceiveStatus = TimecodeBridge.Models.TimecodeReceiveStatus;

#region Test Doubles

internal class StubTimecodeEngine : ITimecodeEngine
{
    public TimecodeValue CurrentRawTimecode { get; set; }
    public TimecodeValue CurrentOffsetTimecode { get; set; }
    public TimecodeOffset Offset { get; set; }
    public FrameRate FrameRate { get; set; } = FrameRate.Fps30;
    public TimecodeSourceType ActiveSource { get; set; }
    public bool IsReceiving { get; set; }
    public double FreerunDurationSeconds { get; set; }
    public bool IsFreerunning { get; set; }
    public LtcSignalCounts LtcSignalCounts { get; set; }
    public bool LtcAutoRecoverOnSignalLoss { get; set; } = true;

    public event EventHandler<TimecodeUpdatedEventArgs>? TimecodeUpdated;
    public event EventHandler<TimecodeStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<AudioSamplesEventArgs>? AudioSamplesAvailable;

    public void StartLtc(string audioDeviceId, bool isLoopback = false) { }
    public void Stop() { }
    public void StartGenerator(GeneratorSettings settings) { }
    public void ResetGenerator() { }
    public void ResetGenerator(TimecodeValue startTime) { }
    public void ResumeGenerator() { }
    public void StopGenerator() { }

    public void RaiseTimecodeUpdated(TimecodeValue raw, TimecodeValue offset)
    {
        CurrentRawTimecode = raw;
        CurrentOffsetTimecode = offset;
        TimecodeUpdated?.Invoke(this, new TimecodeUpdatedEventArgs(raw, offset));
    }

    public void RaiseStatusChanged(bool isReceiving)
    {
        IsReceiving = isReceiving;
        StatusChanged?.Invoke(this, new TimecodeStatusChangedEventArgs(isReceiving));
    }

    public void RaiseStatusChanged(TimecodeReceiveStatus status)
    {
        IsReceiving = status != TimecodeReceiveStatus.NotReceiving;
        IsFreerunning = status == TimecodeReceiveStatus.Freerunning;
        StatusChanged?.Invoke(this, new TimecodeStatusChangedEventArgs(status));
    }
}

internal class SpyOscSender : IOscSender
{
    public List<(string OscAddress, IReadOnlyList<OscArgument> Arguments, IReadOnlyList<string> TargetHostIds)> SentMessages { get; } = [];

    public event EventHandler<OscSendResultEventArgs>? SendCompleted;

    public void Send(string oscAddress, IReadOnlyList<OscArgument> arguments, IReadOnlyList<string> targetHostIds)
    {
        SentMessages.Add((oscAddress, arguments, targetHostIds));
    }

    public void SendPing(string hostId) { }
    public Task SendIcmpPingAsync(string hostId, int framesPerSecond) => Task.CompletedTask;

    // Suppress unused warning
    protected void OnSendCompleted(OscSendResultEventArgs e) => SendCompleted?.Invoke(this, e);
}

#endregion

public class TimecodeRelayTests
{
    private readonly StubTimecodeEngine _engine;
    private readonly SpyOscSender _oscSender;
    private readonly TimecodeRelay _relay;

    private static readonly TimecodeValue SampleTimecode = new(1, 2, 3, 4, FrameRate.Fps30);

    public TimecodeRelayTests()
    {
        _engine = new StubTimecodeEngine();
        _oscSender = new SpyOscSender();
        _relay = new TimecodeRelay(_engine, _oscSender);

        // Default setup: continuous enabled, EveryFrame, one target
        _relay.IsContinuousEnabled = true;
        _relay.ContinuousInterval = new RelayInterval(RelayIntervalMode.EveryFrame, 0);
        _relay.TargetHostIds = ["host1"];
        _relay.OscAddressPattern = "/timecode";
    }

    #region 7.1 Continuous Relay

    [Fact]
    public void ContinuousEnabled_TimecodeUpdated_SendsOsc()
    {
        _relay.IsContinuousEnabled = true;

        _engine.RaiseTimecodeUpdated(SampleTimecode, SampleTimecode);

        Assert.Single(_oscSender.SentMessages);
        var msg = _oscSender.SentMessages[0];
        Assert.Equal("/timecode", msg.OscAddress);
        Assert.Single(msg.Arguments);
        var arg = Assert.IsType<OscFloat32Argument>(msg.Arguments[0]);
        Assert.Equal(SampleTimecode.TotalFrames() / (float)SampleTimecode.FrameRate.FramesPerSecond(), arg.Value);
    }

    [Fact]
    public void ContinuousDisabled_TimecodeUpdated_DoesNotSend()
    {
        _relay.IsContinuousEnabled = false;

        _engine.RaiseTimecodeUpdated(SampleTimecode, SampleTimecode);

        Assert.Empty(_oscSender.SentMessages);
    }

    [Fact]
    public void EveryFrameMode_SendsEveryFrame()
    {
        _relay.ContinuousInterval = new RelayInterval(RelayIntervalMode.EveryFrame, 0);

        var tc1 = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30);
        var tc2 = new TimecodeValue(1, 0, 0, 1, FrameRate.Fps30);
        var tc3 = new TimecodeValue(1, 0, 0, 2, FrameRate.Fps30);

        _engine.RaiseTimecodeUpdated(tc1, tc1);
        _engine.RaiseTimecodeUpdated(tc2, tc2);
        _engine.RaiseTimecodeUpdated(tc3, tc3);

        Assert.Equal(3, _oscSender.SentMessages.Count);
    }

    [Fact]
    public void CustomMode_IntervalNotElapsed_DoesNotSend()
    {
        _relay.ContinuousInterval = new RelayInterval(RelayIntervalMode.Custom, 1000);

        _engine.RaiseTimecodeUpdated(SampleTimecode, SampleTimecode);
        // First send should go through
        Assert.Single(_oscSender.SentMessages);

        // Immediately send again - should be throttled
        _engine.RaiseTimecodeUpdated(SampleTimecode, SampleTimecode);
        Assert.Single(_oscSender.SentMessages);
    }

    [Fact]
    public async Task CustomMode_IntervalElapsed_Sends()
    {
        _relay.ContinuousInterval = new RelayInterval(RelayIntervalMode.Custom, 50);

        _engine.RaiseTimecodeUpdated(SampleTimecode, SampleTimecode);
        Assert.Single(_oscSender.SentMessages);

        await Task.Delay(80);

        _engine.RaiseTimecodeUpdated(SampleTimecode, SampleTimecode);
        Assert.Equal(2, _oscSender.SentMessages.Count);
    }

    [Fact]
    public void CustomOscAddressPattern_UsedInSend()
    {
        _relay.OscAddressPattern = "/my/custom/tc";

        _engine.RaiseTimecodeUpdated(SampleTimecode, SampleTimecode);

        Assert.Single(_oscSender.SentMessages);
        Assert.Equal("/my/custom/tc", _oscSender.SentMessages[0].OscAddress);
    }

    [Fact]
    public void TargetHostIds_PassedCorrectly()
    {
        _relay.TargetHostIds = ["hostA", "hostB", "hostC"];

        _engine.RaiseTimecodeUpdated(SampleTimecode, SampleTimecode);

        Assert.Single(_oscSender.SentMessages);
        Assert.Equal(["hostA", "hostB", "hostC"], _oscSender.SentMessages[0].TargetHostIds);
    }

    [Fact]
    public void SignalLost_StopsRelayAndSendsNotification()
    {
        // First, send some timecode
        _engine.RaiseTimecodeUpdated(SampleTimecode, SampleTimecode);
        Assert.Single(_oscSender.SentMessages);

        // Signal lost
        _engine.RaiseStatusChanged(false);

        // Should have sent a lost notification
        Assert.Equal(2, _oscSender.SentMessages.Count);
        var lostMsg = _oscSender.SentMessages[1];
        Assert.Equal("/timecode/lost", lostMsg.OscAddress);

        // Subsequent timecode updates should not send (because IsContinuousEnabled is still true
        // but signal is lost - the relay should still work when new frames arrive)
        // Actually, signal lost just sends notification. New frames can still come.
        // Let's verify that after signal lost, if new frames come, they still send.
    }

    [Fact]
    public void SignalLost_SubsequentTimecodeUpdates_StillWork()
    {
        _engine.RaiseTimecodeUpdated(SampleTimecode, SampleTimecode);
        _engine.RaiseStatusChanged(false);
        _oscSender.SentMessages.Clear();

        // New frame arrives - should still relay
        _engine.RaiseTimecodeUpdated(SampleTimecode, SampleTimecode);
        Assert.Single(_oscSender.SentMessages);
    }

    [Fact]
    public void OffsetTimecodeUsed_NotRaw()
    {
        var raw = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30);
        var offset = new TimecodeValue(1, 0, 5, 0, FrameRate.Fps30);

        _engine.RaiseTimecodeUpdated(raw, offset);

        Assert.Single(_oscSender.SentMessages);
        var arg = Assert.IsType<OscFloat32Argument>(_oscSender.SentMessages[0].Arguments[0]);
        Assert.Equal(offset.TotalFrames() / (float)offset.FrameRate.FramesPerSecond(), arg.Value);
    }

    [Fact]
    public void Freerunning_DoesNotSendLostNotification()
    {
        _engine.RaiseTimecodeUpdated(SampleTimecode, SampleTimecode);
        _oscSender.SentMessages.Clear();

        // Freerunning status should NOT send /timecode/lost
        _engine.RaiseStatusChanged(TimecodeReceiveStatus.Freerunning);

        Assert.Empty(_oscSender.SentMessages);
    }

    [Fact]
    public void NotReceiving_SendsLostNotification()
    {
        _engine.RaiseTimecodeUpdated(SampleTimecode, SampleTimecode);
        _oscSender.SentMessages.Clear();

        // NotReceiving should send /timecode/lost
        _engine.RaiseStatusChanged(TimecodeReceiveStatus.NotReceiving);

        Assert.Single(_oscSender.SentMessages);
        Assert.Equal("/timecode/lost", _oscSender.SentMessages[0].OscAddress);
    }

    #endregion

    #region 7.2 One-Shot

    [Fact]
    public void TriggerOneShot_SendsOnce()
    {
        _engine.CurrentOffsetTimecode = SampleTimecode;

        _relay.TriggerOneShot();

        Assert.Single(_oscSender.SentMessages);
        var msg = _oscSender.SentMessages[0];
        Assert.Equal("/timecode", msg.OscAddress);
        Assert.Single(msg.Arguments);
        var arg = Assert.IsType<OscFloat32Argument>(msg.Arguments[0]);
        Assert.Equal(SampleTimecode.TotalFrames() / (float)SampleTimecode.FrameRate.FramesPerSecond(), arg.Value);
    }

    [Fact]
    public void TriggerOneShot_UsesCurrentOffsetTimecode()
    {
        var tc = new TimecodeValue(10, 20, 30, 15, FrameRate.Fps30);
        _engine.CurrentOffsetTimecode = tc;

        _relay.TriggerOneShot();

        Assert.Single(_oscSender.SentMessages);
        var arg = Assert.IsType<OscFloat32Argument>(_oscSender.SentMessages[0].Arguments[0]);
        Assert.Equal(tc.TotalFrames() / (float)tc.FrameRate.FramesPerSecond(), arg.Value);
    }

    [Fact]
    public void TriggerOneShot_UsesConfiguredAddressAndTargets()
    {
        _relay.OscAddressPattern = "/custom/tc";
        _relay.TargetHostIds = ["target1", "target2"];
        _engine.CurrentOffsetTimecode = SampleTimecode;

        _relay.TriggerOneShot();

        Assert.Single(_oscSender.SentMessages);
        var msg = _oscSender.SentMessages[0];
        Assert.Equal("/custom/tc", msg.OscAddress);
        Assert.Equal(["target1", "target2"], msg.TargetHostIds);
    }

    [Fact]
    public void TriggerOneShot_DoesNotAffectContinuousRelay()
    {
        _relay.IsContinuousEnabled = false;
        _engine.CurrentOffsetTimecode = SampleTimecode;

        _relay.TriggerOneShot();

        // One-shot should work regardless of IsContinuousEnabled
        Assert.Single(_oscSender.SentMessages);
    }

    #endregion
}
