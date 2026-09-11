using Avalonia.Headless.XUnit;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Host.Bridge;
using Xunit;

namespace TimecodeBridge.Host.Tests;

public class HostStateTests
{
    [AvaloniaFact]
    public void FlushEmitsOnlyDirtyDomainsAndAdvancesRevision()
    {
        var h = new HostHarness();
        StateChanges? got = null; long rev = -1, baseRev = -1;
        h.State.Changed += (c, r, b) => { got = c; rev = r; baseRev = b; };

        h.State.MarkDirty(Domain.Receive);
        h.State.Flush();

        Assert.NotNull(got);
        Assert.NotNull(got!.Receive);
        Assert.Null(got.Cues);
        Assert.Null(got.Transport);
        Assert.Equal(0, baseRev);
        Assert.Equal(1, rev);
        Assert.Equal(1, h.State.Revision);

        got = null;
        h.State.Flush();
        Assert.Null(got);
    }

    [AvaloniaFact]
    public void TransportReflectsEngineStatus()
    {
        var h = new HostHarness();
        h.State.LtcStarted = true;
        h.State.SelectedInputDeviceId = "in-1";
        h.Engine.RaiseStatus(TimecodeReceiveStatus.Receiving);
        var snap = h.State.BuildSnapshot();
        Assert.Equal(TransportStatus.Receiving, snap.Transport.Status);
        Assert.Equal("Audio Bridge 2-A", snap.Transport.DetailText);

        h.Engine.RaiseStatus(TimecodeReceiveStatus.NotReceiving);
        Assert.Equal(TransportStatus.SignalLost, h.State.BuildSnapshot().Transport.Status);

        h.Engine.RaiseStatus(TimecodeReceiveStatus.Freerunning);
        Assert.Equal(TransportStatus.Freerun, h.State.BuildSnapshot().Transport.Status);
    }

    [AvaloniaFact]
    public void ClockUsesLatestUpdateAndNextCue()
    {
        var h = new HostHarness();
        h.Cues.AddCue(new Cue { Id = "c1", Name = "A", TriggerTime = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30), OscAddress = "/a" });
        h.Cues.AddCue(new Cue { Id = "c2", Name = "B", TriggerTime = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30), OscAddress = "/b", IsEnabled = false });
        h.Cues.AddCue(new Cue { Id = "c3", Name = "C", TriggerTime = new TimecodeValue(0, 0, 8, 0, FrameRate.Fps30), OscAddress = "/c" });

        h.Engine.RaiseTimecode(new TimecodeValue(0, 0, 6, 0, FrameRate.Fps30), new TimecodeValue(0, 0, 7, 0, FrameRate.Fps30));
        var clock = h.State.BuildClock();
        Assert.Equal("00:00:06:00", clock.Raw);
        Assert.Equal("00:00:07:00", clock.Display);
        Assert.Equal("c3", clock.NextCueId);
        Assert.Equal(30, clock.FramesUntilNextCue);
    }

    [AvaloniaFact]
    public void OscResultsBecomeLogsCappedAt500()
    {
        var h = new HostHarness();
        for (int i = 0; i < 520; i++) h.Osc.Complete("/x", "h1", i % 2 == 0);
        h.State.Flush();
        var snap = h.State.BuildSnapshot();
        Assert.Equal(500, snap.Logs.Count);
        Assert.Contains("失敗", snap.Logs[^1].Message);
    }

    [Fact]
    public void ParsesTimecodeStrictly()
    {
        Assert.True(HostState.TryParseTimecode("01:02:03:04", FrameRate.Fps30, out var tc));
        Assert.Equal(new TimecodeValue(1, 2, 3, 4, FrameRate.Fps30), tc);
        Assert.True(HostState.TryParseTimecode("00:01:00;02", FrameRate.Fps2997Drop, out _));
        Assert.False(HostState.TryParseTimecode("01:02:03:30", FrameRate.Fps30, out _));
        Assert.False(HostState.TryParseTimecode("01:02:03", FrameRate.Fps30, out _));
        Assert.False(HostState.TryParseTimecode("-01:02:03:00", FrameRate.Fps30, out _));
    }

    [Fact]
    public void FullWidthDigitsAndSeparatorsAreNormalized()
    {
        Assert.True(HostState.TryParseTimecode("０１：０２：０３：０４", FrameRate.Fps30, out var tc));
        Assert.Equal(new TimecodeValue(1, 2, 3, 4, FrameRate.Fps30), tc);
        Assert.Equal("-00:00:01:00", HostState.NormalizeTimecodeText("－００：００：０１：００"));
        Assert.Equal("+00:00:00:05", HostState.NormalizeTimecodeText(" ＋00:00:00:05 "));
    }

    [Fact]
    public void WaveReducerReducesToMinMaxAndReportsLevel()
    {
        var w = new WaveReducer();
        w.Configure(48000, 100);
        var samples = new float[4800];
        for (int i = 0; i < samples.Length; i++) samples[i] = i < 2400 ? 0.5f : -0.25f;
        samples[100] = float.NaN;
        w.Write(samples);

        // 点数は 16 未満に丸められる(表示幅が極端に狭くても最低限の形は出す)
        var snap = w.Snapshot(2)!.Value;
        Assert.Equal(16, snap.Min.Length);
        Assert.Equal(0.5f, snap.Max[0]);
        Assert.Equal(0.5f, snap.Min[0]);
        Assert.Equal(-0.25f, snap.Min[^1]);
        Assert.Equal(-0.25f, snap.Max[^1]);
        Assert.NotNull(snap.LevelDbfs);
        Assert.InRange(snap.LevelDbfs!.Value, -6.1, -6.0);

        Assert.Null(w.Snapshot(16)!.Value.LevelDbfs);
    }
}
