using Avalonia.Headless.XUnit;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Host.Bridge;
using Xunit;

namespace TimecodeBridge.Host.Tests;

public class CommandRouterTests
{
    [AvaloniaFact]
    public void CueFireReportsDisabledMissingAndMixedTargets()
    {
        static System.Text.Json.JsonElement Data(ResultMessage result) => System.Text.Json.JsonSerializer.SerializeToElement(result.Data, Protocol.Json);
        var h = new HostHarness();
        h.Hosts.AddHost(new OscHost { Id = "off", Name = "無効Host", IpAddress = "127.0.0.1", Port = 9000, IsEnabled = false });
        h.Hosts.AddHost(new OscHost { Id = "on", Name = "有効Host", IpAddress = "127.0.0.1", Port = 9001, IsEnabled = true });

        ResultMessage Fire(string id, string targets)
        {
            var add = h.Run("cue.add", $$$"""{"cue":{"name":"{{{id}}}","triggerTime":"00:00:01:00","oscAddress":"/go","targetHostIds":{{{targets}}}}}""");
            var cueId = Data(add).GetProperty("id").GetString();
            return h.Run("cue.fire", System.Text.Json.JsonSerializer.Serialize(new { id = cueId }, Protocol.Json));
        }

        var disabled = Data(Fire("disabled", "[\"off\"]"));
        Assert.False(disabled.GetProperty("sent").GetBoolean());
        Assert.Equal(0, disabled.GetProperty("sentCount").GetInt32());
        Assert.Equal("off", disabled.GetProperty("skippedHostIds")[0].GetString());
        Assert.Contains(h.State.BuildSnapshot().Logs, l => !l.Success && l.Message.Contains("送信先がありません(無効: 無効Host)"));

        var missing = Data(Fire("missing", "[\"missing\"]"));
        Assert.False(missing.GetProperty("sent").GetBoolean());
        Assert.Equal("missing", missing.GetProperty("skippedHostIds")[0].GetString());

        var mixed = Data(Fire("mixed", "[\"on\",\"off\",\"missing\"]"));
        Assert.True(mixed.GetProperty("sent").GetBoolean());
        Assert.Equal(1, mixed.GetProperty("sentCount").GetInt32());
        Assert.Equal(2, mixed.GetProperty("skippedHostIds").GetArrayLength());
    }

    [AvaloniaFact]
    public void TriggerPanelAndCueSyncReturnDispatchDetails()
    {
        static System.Text.Json.JsonElement Data(ResultMessage result) => System.Text.Json.JsonSerializer.SerializeToElement(result.Data, Protocol.Json);
        var h = new HostHarness();
        h.Hosts.AddHost(new OscHost { Id = "off", Name = "無効Host", IpAddress = "127.0.0.1", Port = 9000, IsEnabled = false });
        Assert.True(h.Run("triggerPanel.upsertButton", """{"button":{"id":"b1","row":0,"column":0,"label":"GO","oscAddress":"/go","targetHostIds":["off"]}}""").Ok);
        var panel = Data(h.Run("triggerPanel.fire", """{"id":"b1"}"""));
        Assert.False(panel.GetProperty("sent").GetBoolean());
        Assert.Equal(0, panel.GetProperty("sentCount").GetInt32());
        Assert.Equal("off", panel.GetProperty("skippedHostIds")[0].GetString());
        Assert.Equal("送信できませんでした", panel.GetProperty("reason").GetString());

        Assert.True(h.Run("cueSync.configure", """{"targetHostIds":["missing"]}""").Ok);
        var sync = Data(h.Run("cueSync.send"));
        Assert.False(sync.GetProperty("sent").GetBoolean());
        Assert.Equal(0, sync.GetProperty("sentCount").GetInt32());
        Assert.Equal("missing", sync.GetProperty("skippedHostIds")[0].GetString());
    }

    [AvaloniaFact]
    public void ChangingOutputOrVolumeWhileGeneratingAppliesImmediatelyAndStartSettingsStayPending()
    {
        var h = new HostHarness();
        Assert.True(h.Run("generator.configure", """{"outputDeviceId":"out-1","volume":0.8}""").Ok);
        Assert.True(h.Run("generator.start").Ok);
        h.Engine.ActiveSource = TimecodeSourceType.Generator;
        Assert.True(h.State.BuildSnapshot().Generator.LtcOutputActive);

        Assert.True(h.Run("generator.configure", """{"outputDeviceId":""}""").Ok);
        Assert.Contains("ApplyGeneratorOutput:", h.Engine.Calls);
        Assert.False(h.State.BuildSnapshot().Generator.LtcOutputActive);
        Assert.False(h.State.GeneratorSettingsPending);

        Assert.True(h.Run("generator.configure", """{"volume":0.3}""").Ok);
        Assert.Contains("SetGeneratorVolume:0.3", h.Engine.Calls);

        Assert.True(h.Run("generator.configure", """{"frameRate":"25"}""").Ok);
        Assert.True(h.State.GeneratorSettingsPending);
        Assert.True(h.State.BuildSnapshot().Generator.SettingsPendingReset);
        Assert.True(h.Run("generator.start").Ok);
        Assert.False(h.State.GeneratorSettingsPending);
    }

    [AvaloniaFact]
    public void ExpectedRevisionMismatchIsRejectedWithoutMutation()
    {
        var h = new HostHarness();
        var current = h.State.Revision;

        var conflict = h.Run("receive.setTriggerWindow", """{"frames":9}""", expectedRevision: current + 1);

        Assert.False(conflict.Ok);
        Assert.Equal(ErrorCode.Conflict, conflict.Error!.Code);
        Assert.True(conflict.Error.Retryable);
        Assert.NotEqual(9, h.Cues.TriggerWindowFrames);
        Assert.True(h.Run("receive.setTriggerWindow", """{"frames":9}""", expectedRevision: current).Ok);
        Assert.Equal(9, h.Cues.TriggerWindowFrames);
    }

    [AvaloniaFact]
    public void UnknownCommandIsRejected()
    {
        var h = new HostHarness();
        var r = h.Run("nope.doIt");
        Assert.False(r.Ok);
        Assert.Equal(ErrorCode.UnknownCommand, r.Error!.Code);
        Assert.Equal("r1", r.RequestId);
    }

    [AvaloniaFact]
    public void SetOffsetNormalizesAndRejectsGarbage()
    {
        var h = new HostHarness();
        var ok = h.Run("receive.setOffset", """{"value":"-0:0:1:0"}""");
        Assert.True(ok.Ok);
        Assert.Equal("-00:00:01:00", h.Engine.Offset.ToString());

        var wide = h.Run("receive.setOffset", """{"value":"＋００：００：００：０５"}""");
        Assert.True(wide.Ok);
        Assert.Equal("+00:00:00:05", h.Engine.Offset.ToString());

        var bad = h.Run("receive.setOffset", """{"value":"abc"}""");
        Assert.False(bad.Ok);
        Assert.Equal(ErrorCode.Validation, bad.Error!.Code);
        Assert.True(bad.Error.FieldErrors!.ContainsKey("value"));
    }

    [AvaloniaFact]
    public void TriggerWindowIsBounded()
    {
        var h = new HostHarness();
        Assert.True(h.Run("receive.setTriggerWindow", """{"frames":3}""").Ok);
        Assert.Equal(3, h.Cues.TriggerWindowFrames);
        Assert.Equal(ErrorCode.Validation, h.Run("receive.setTriggerWindow", """{"frames":-1}""").Error!.Code);
        Assert.Equal(ErrorCode.Validation, h.Run("receive.setTriggerWindow", """{"frames":"3"}""").Error!.Code);
        Assert.Equal(3, h.Cues.TriggerWindowFrames);
    }

    [AvaloniaFact]
    public void ModeSetStopsEngineAndSwitchesMode()
    {
        var h = new HostHarness();
        Assert.True(h.Run("mode.set", """{"mode":"generate"}""").Ok);
        Assert.Contains("Stop", h.Engine.Calls);
        Assert.Equal("generate", h.State.Mode);
        Assert.Equal(ErrorCode.Validation, h.Run("mode.set", """{"mode":"vinyl"}""").Error!.Code);
    }

    [AvaloniaFact]
    public void SwitchingBackToLtcReconnectsSelectedDevice()
    {
        var h = new HostHarness();
        Assert.True(h.Run("ltc.reconnect", """{"deviceId":"in-1"}""").Ok);
        Assert.True(h.Run("mode.set", """{"mode":"generate"}""").Ok);
        Assert.False(h.State.LtcStarted);
        h.Engine.Calls.Clear();

        Assert.True(h.Run("mode.set", """{"mode":"ltc"}""").Ok);
        Assert.Contains("StartLtc:in-1:False", h.Engine.Calls);
        Assert.True(h.State.LtcStarted);
        Assert.Equal("in-1", h.State.SelectedInputDeviceId);

        // デバイス未選択なら停止のまま(エラーにはしない)
        var h2 = new HostHarness();
        Assert.True(h2.Run("mode.set", """{"mode":"ltc"}""").Ok);
        Assert.False(h2.State.LtcStarted);
        Assert.DoesNotContain(h2.Engine.Calls, c => c.StartsWith("StartLtc"));
    }

    [AvaloniaFact]
    public void ReturningFromDropFrameGeneratorRestoresLastLtcClockImmediately()
    {
        var h = new HostHarness();
        Assert.True(h.Run("ltc.reconnect", """{"deviceId":"in-1"}""").Ok);
        h.Engine.IsReceiving = true;
        h.Engine.RaiseTimecode(
            new TimecodeValue(1, 0, 5, 12, FrameRate.Fps30),
            new TimecodeValue(1, 0, 5, 12, FrameRate.Fps30));

        Assert.True(h.Run("mode.set", """{"mode":"generate"}""").Ok);
        h.Engine.FrameRate = FrameRate.Fps2997Drop;
        h.Engine.RaiseTimecode(
            new TimecodeValue(10, 0, 0, 18, FrameRate.Fps2997Drop),
            new TimecodeValue(10, 0, 0, 18, FrameRate.Fps2997Drop));

        Assert.True(h.Run("mode.set", """{"mode":"ltc"}""").Ok);
        var clock = h.State.BuildClock();
        Assert.Equal(FrameRate.Fps30, h.Engine.FrameRate);
        Assert.Equal(FrameRateCode.Fps30, clock.FrameRate);
        Assert.False(clock.DropFrame);
        Assert.Equal("01:00:05:12", clock.Display);
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void SelectDeviceCanReturnToUnselected()
    {
        var h = new HostHarness();
        Assert.True(h.Run("receive.selectDevice", """{"deviceId":"loop-1"}""").Ok);
        Assert.Contains("StartLtc:loop-1:True", h.Engine.Calls);
        Assert.Equal("loop-1", h.State.SelectedInputDeviceId);

        Assert.Equal(ErrorCode.DeviceNotFound, h.Run("receive.selectDevice", """{"deviceId":"ghost"}""").Error!.Code);
        Assert.Equal("loop-1", h.State.SelectedInputDeviceId);

        h.Engine.Calls.Clear();
        foreach (var args in new[] { """{"deviceId":null}""", """{"deviceId":""}""", "{}" })
        {
            Assert.True(h.Run("receive.selectDevice", args).Ok);
            Assert.Contains("Stop", h.Engine.Calls);
            Assert.False(h.State.LtcStarted);
            Assert.Null(h.State.SelectedInputDeviceId);
        }

        // 未選択のまま LTC 受信モードへ戻っても自動再接続はしない
        Assert.True(h.Run("mode.set", """{"mode":"generate"}""").Ok);
        h.Engine.Calls.Clear();
        var back = h.Run("mode.set", """{"mode":"ltc"}""");
        Assert.True(back.Ok);
        Assert.DoesNotContain(h.Engine.Calls, c => c.StartsWith("StartLtc"));
        Assert.False(h.State.LtcStarted);
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void CueSyncConfigureValidatesAddressAndSendUsesIt()
    {
        var h = new HostHarness();
        h.Hosts.AddHost(new OscHost { Id = "h1", Name = "QLab", IpAddress = "127.0.0.1", Port = 53000 });
        Assert.Equal(ErrorCode.Validation, h.Run("cueSync.configure", """{"oscAddress":"nope"}""").Error!.Code);
        Assert.True(h.Run("cueSync.configure", """{"oscAddress":"/sync","targetHostIds":["h1"]}""").Ok);
        Assert.True(h.Run("cueSync.send").Ok);
        Assert.Contains(h.Osc.Sent, s => s.Address == "/sync" && s.Hosts.Contains("h1"));
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
    public async Task HostPingReportsReachabilityAndLatency()
    {
        var h = new HostHarness();
        h.Hosts.AddHost(new OscHost { Id = "lo", Name = "Local", IpAddress = "127.0.0.1", Port = 9000 });
        var r = await h.RunAsync("host.ping", """{"id":"lo"}""");
        Assert.True(r.Ok, r.Error?.Message);
        var data = System.Text.Json.JsonSerializer.SerializeToElement(r.Data, Protocol.Json);
        Assert.True(data.GetProperty("reachable").GetBoolean());
        Assert.True(data.GetProperty("latencyMs").GetInt64() >= 0);
        Assert.Equal("reachable", h.State.BuildSnapshot().Hosts[0].Reachability);
        Assert.Equal(ErrorCode.NotFound, h.Run("host.ping", """{"id":"none"}""").Error!.Code);
    }

    [AvaloniaFact]
    public void MuteSetTogglesCueManager()
    {
        var h = new HostHarness();
        Assert.True(h.Run("mute.set", """{"muted":true}""").Ok);
        Assert.True(h.Cues.IsMuted);
        Assert.Equal(ErrorCode.Validation, h.Run("mute.set", """{"muted":"yes"}""").Error!.Code);
    }

    [AvaloniaFact]
    public void EmptyCueDraftIsRejectedAsValidation()
    {
        var h = new HostHarness();
        var r = h.Run("cue.add", """{"cue":{}}""");
        Assert.Equal(ErrorCode.Validation, r.Error!.Code);
        Assert.Equal("cue.name", r.Error.FieldErrors!.Keys.Single());
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
    public async Task DeviceListsAreEnumeratedOnceUntilRescan()
    {
        var h = new HostHarness();
        h.State.BuildSnapshot();
        var afterFirst = h.Devices.Enumerations;
        h.State.BuildSnapshot();
        h.Run("mode.set", """{"mode":"generate"}""");
        h.State.SelectedInputDeviceId = "in-1";
        h.Run("mode.set", """{"mode":"ltc"}""");
        h.State.BuildSnapshot();
        Assert.Equal(afterFirst, h.Devices.Enumerations); // 状態を組み立てても、切り替えても列挙し直さない

        h.Devices.Capture.Add(new("in-2", "UR22C", false));
        Assert.DoesNotContain(h.State.BuildSnapshot().Receive.Devices, d => d.Id == "in-2");
        Assert.True((await h.RunAsync("audio.refreshDevices", """{"direction":"capture"}""")).Ok); // 再スキャンで反映
        Assert.Contains(h.State.BuildSnapshot().Receive.Devices, d => d.Id == "in-2");
    }

    [AvaloniaFact(Timeout = 10_000)]
    public async Task RescanRunsInBackgroundAndKeepsOldListUntilDone()
    {
        var h = new HostHarness();
        h.State.BuildSnapshot();
        using var gate = new ManualResetEventSlim(false);
        h.Devices.Gate = gate; // 以後の列挙は gate が開くまで終わらない
        h.Devices.Capture.Add(new("in-2", "UR22C", false));

        var rescan = h.RunAsync("audio.refreshDevices", """{"direction":"capture"}""");
        Assert.False(rescan.IsCompleted); // 列挙が終わるまで result を返さない(Web はその間スキャン中を表示する)
        Assert.DoesNotContain(h.State.BuildSnapshot().Receive.Devices, d => d.Id == "in-2"); // 列挙中も止まらず古い一覧を返す
        Assert.True(h.Run("mode.set", """{"mode":"generate"}""").Ok); // ほかの command も止まらない

        gate.Set();
        Assert.True((await rescan).Ok);
        Assert.Contains(h.State.BuildSnapshot().Receive.Devices, d => d.Id == "in-2");
    }

    [AvaloniaFact]
    public void SelectingDeviceMissingFromCachedListRescansOnce()
    {
        var h = new HostHarness();
        h.State.BuildSnapshot();
        h.Devices.Capture.Add(new("in-2", "UR22C", false)); // 起動後に挿したデバイス(再スキャン前)
        Assert.True(h.Run("receive.selectDevice", """{"deviceId":"in-2"}""").Ok);
        Assert.Contains("StartLtc:in-2:False", h.Engine.Calls);
        Assert.Equal(ErrorCode.DeviceNotFound, h.Run("receive.selectDevice", """{"deviceId":"none"}""").Error!.Code);
    }

    [AvaloniaFact]
    public void GeneratorStartAfterPauseAppliesPendingSettingsInsteadOfResuming()
    {
        var h = new HostHarness();
        h.Run("generator.configure", """{"startTime":"01:00:00:00","frameRate":"30"}""");
        h.Run("generator.start");
        h.Engine.ActiveSource = TimecodeSourceType.Generator;
        h.Engine.CurrentRawTimecode = new TimecodeValue(1, 0, 5, 0, FrameRate.Fps30);
        h.Run("generator.stop");

        Assert.True(h.Run("generator.start").Ok); // 設定を変えていなければ止めた位置から再開
        Assert.Equal("ResumeGenerator", h.Engine.Calls[^1]);

        h.Run("generator.stop");
        h.Run("generator.configure", """{"startTime":"02:00:00:00","frameRate":"25"}""");
        Assert.True(h.State.GeneratorSettingsPending);
        Assert.True(h.Run("generator.start").Ok); // 保留中の変更があれば新しい設定で最初から
        Assert.Equal("StartGenerator:02:00:00:00", h.Engine.Calls[^1]);
        Assert.False(h.State.GeneratorSettingsPending);
    }

    [AvaloniaFact]
    public void GeneratorResetRebuildsWhenFrameRateChanged()
    {
        var h = new HostHarness();
        h.Run("generator.configure", """{"startTime":"01:00:00:00","frameRate":"30"}""");
        h.Run("generator.start");
        h.Engine.ActiveSource = TimecodeSourceType.Generator;
        h.Engine.FrameRate = FrameRate.Fps30;

        h.Run("generator.configure", """{"startTime":"03:00:00:00"}""");
        Assert.True(h.Run("generator.reset").Ok); // レートが同じなら開始 TC だけ差し替える
        Assert.Equal("ResetGenerator:03:00:00:00", h.Engine.Calls[^1]);

        h.Run("generator.configure", """{"frameRate":"24"}""");
        Assert.True(h.Run("generator.reset").Ok); // レートが変われば生成器ごと作り直す
        Assert.Equal("StartGenerator:03:00:00:00", h.Engine.Calls[^1]);
        Assert.False(h.State.GeneratorSettingsPending);

        h.Run("generator.stop");
        h.Engine.FrameRate = FrameRate.Fps24;
        h.Run("generator.configure", """{"frameRate":"25"}""");
        Assert.True(h.Run("generator.reset").Ok); // 一時停止中なら作り直したあと開始位置で止める
        Assert.Equal(new[] { "StartGenerator:03:00:00:00", "StopGenerator" }, h.Engine.Calls.TakeLast(2));
    }
}
