using TimecodeBridge.Core.Models;
using TimecodeBridge.Host.Bridge;
using Xunit;

namespace TimecodeBridge.Host.Tests;

public class CommandRouterTests
{
    [Fact]
    public void UnknownCommandIsRejected()
    {
        var h = new HostHarness();
        var r = h.Run("nope.doIt");
        Assert.False(r.Ok);
        Assert.Equal(ErrorCode.UnknownCommand, r.Error!.Code);
        Assert.Equal("r1", r.RequestId);
    }

    [Fact]
    public void SetOffsetNormalizesAndRejectsGarbage()
    {
        var h = new HostHarness();
        var ok = h.Run("receive.setOffset", """{"value":"-0:0:1:0"}""");
        Assert.True(ok.Ok);
        Assert.Equal("-00:00:01:00", h.Engine.Offset.ToString());

        var bad = h.Run("receive.setOffset", """{"value":"abc"}""");
        Assert.False(bad.Ok);
        Assert.Equal(ErrorCode.Validation, bad.Error!.Code);
        Assert.True(bad.Error.FieldErrors!.ContainsKey("value"));
    }

    [Fact]
    public void TriggerWindowIsBounded()
    {
        var h = new HostHarness();
        Assert.True(h.Run("receive.setTriggerWindow", """{"frames":3}""").Ok);
        Assert.Equal(3, h.Cues.TriggerWindowFrames);
        Assert.Equal(ErrorCode.Validation, h.Run("receive.setTriggerWindow", """{"frames":-1}""").Error!.Code);
        Assert.Equal(ErrorCode.Validation, h.Run("receive.setTriggerWindow", """{"frames":"3"}""").Error!.Code);
        Assert.Equal(3, h.Cues.TriggerWindowFrames);
    }

    [Fact]
    public void ModeSetStopsEngineAndSwitchesMode()
    {
        var h = new HostHarness();
        Assert.True(h.Run("mode.set", """{"mode":"generate"}""").Ok);
        Assert.Contains("Stop", h.Engine.Calls);
        Assert.Equal("generate", h.State.Mode);
        Assert.Equal(ErrorCode.Validation, h.Run("mode.set", """{"mode":"vinyl"}""").Error!.Code);
    }

    [Fact]
    public void LtcReconnectRequiresKnownDevice()
    {
        var h = new HostHarness();
        var missing = h.Run("ltc.reconnect", """{"deviceId":"ghost"}""");
        Assert.Equal(ErrorCode.DeviceNotFound, missing.Error!.Code);
        Assert.False(h.State.LtcStarted);

        var ok = h.Run("ltc.reconnect", """{"deviceId":"loop-1"}""");
        Assert.True(ok.Ok);
        Assert.Contains("StartLtc:loop-1:True", h.Engine.Calls);
        Assert.True(h.State.LtcStarted);
        Assert.Equal("loop-1", h.State.SelectedInputDeviceId);

        // 2 回目はデバイス省略で前回のデバイスに再接続する
        h.Engine.Calls.Clear();
        Assert.True(h.Run("ltc.reconnect").Ok);
        Assert.Contains("StartLtc:loop-1:True", h.Engine.Calls);

        Assert.True(h.Run("ltc.stop").Ok);
        Assert.False(h.State.LtcStarted);
    }

    [Fact]
    public void CueFireAndEnableUseCueManager()
    {
        var h = new HostHarness();
        h.Hosts.AddHost(new OscHost { Id = "h1", Name = "QLab", IpAddress = "127.0.0.1", Port = 53000 });
        h.Cues.AddCue(new Cue { Id = "c1", Name = "GO", TriggerTime = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30), OscAddress = "/go", TargetHostIds = ["h1"] });

        Assert.Equal(ErrorCode.NotFound, h.Run("cue.fire", """{"id":"zzz"}""").Error!.Code);
        Assert.True(h.Run("cue.fire", """{"id":"c1"}""").Ok);
        Assert.Contains(h.Osc.Sent, s => s.Address == "/go");

        Assert.True(h.Run("cue.setEnabled", """{"id":"c1","enabled":false}""").Ok);
        Assert.False(h.Cues.Cues[0].IsEnabled);
    }

    [Fact]
    public void CueSyncConfigureValidatesAddressAndSendUsesIt()
    {
        var h = new HostHarness();
        h.Hosts.AddHost(new OscHost { Id = "h1", Name = "QLab", IpAddress = "127.0.0.1", Port = 53000 });
        Assert.Equal(ErrorCode.Validation, h.Run("cueSync.configure", """{"oscAddress":"nope"}""").Error!.Code);
        Assert.True(h.Run("cueSync.configure", """{"oscAddress":"/sync","targetHostIds":["h1"]}""").Ok);
        Assert.True(h.Run("cueSync.send").Ok);
        Assert.Contains(h.Osc.Sent, s => s.Address == "/sync" && s.Hosts.Contains("h1"));
    }

    [Fact]
    public void HostAddValidatesAndRegisters()
    {
        var h = new HostHarness();
        Assert.Equal(ErrorCode.Validation, h.Run("host.add", """{"host":{"name":"x","ipAddress":"300.1.1.1","port":9000}}""").Error!.Code);
        Assert.Equal(ErrorCode.Validation, h.Run("host.add", """{"host":{"name":"x","ipAddress":"10.0.0.1","port":70000}}""").Error!.Code);
        var ok = h.Run("host.add", """{"host":{"name":"Light","ipAddress":"10.0.0.1","port":9000}}""");
        Assert.True(ok.Ok);
        Assert.Single(h.Hosts.Hosts);
        Assert.Equal("Light", h.Hosts.Hosts[0].Name);
    }

    [Fact]
    public void MuteSetTogglesCueManager()
    {
        var h = new HostHarness();
        Assert.True(h.Run("mute.set", """{"muted":true}""").Ok);
        Assert.True(h.Cues.IsMuted);
        Assert.Equal(ErrorCode.Validation, h.Run("mute.set", """{"muted":"yes"}""").Error!.Code);
    }

    [Fact]
    public void UnimplementedCommandsReportInvalidState()
    {
        var h = new HostHarness();
        var r = h.Run("cue.add", """{"cue":{}}""");
        Assert.Equal(ErrorCode.InvalidState, r.Error!.Code);
    }

    [Fact]
    public void GeneratorConfigureAndStart()
    {
        var h = new HostHarness();
        Assert.Equal(ErrorCode.Validation, h.Run("generator.configure", """{"startTime":"25:00:00:00"}""").Error!.Code);
        Assert.True(h.Run("generator.configure", """{"startTime":"01:00:00:00","frameRate":"25","volume":0.5}""").Ok);
        Assert.Equal(FrameRate.Fps25, h.State.Generator.FrameRate);
        Assert.Equal("01:00:00:00", h.State.Generator.StartTime.ToString());
        Assert.True(h.Run("generator.start").Ok);
        Assert.Contains("StartGenerator:01:00:00:00", h.Engine.Calls);
        Assert.True(h.State.GeneratorRunning);
        Assert.True(h.Run("generator.stop").Ok);
        Assert.False(h.State.GeneratorRunning);
    }
}
