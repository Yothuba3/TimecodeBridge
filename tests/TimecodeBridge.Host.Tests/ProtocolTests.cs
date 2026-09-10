using System.Text.Json;
using Avalonia.Headless.XUnit;
using TimecodeBridge.Host.Bridge;
using Xunit;

namespace TimecodeBridge.Host.Tests;

public class ProtocolTests
{
    [Fact]
    public void HostMessagesCarryVersionTypeAndCamelCase()
    {
        var msg = new ClockMessage(7, new ClockState("01:00:00:00", "01:00:00:05", 108005, "30", false, 12.5, "cue-1", 30));
        var json = JsonSerializer.Serialize(msg, msg.GetType(), Protocol.Json);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("protocolVersion").GetInt32());
        Assert.Equal("clock", root.GetProperty("type").GetString());
        Assert.Equal(7, root.GetProperty("seq").GetInt64());
        Assert.Equal("cue-1", root.GetProperty("clock").GetProperty("nextCueId").GetString());
        Assert.DoesNotContain("Raw\"", json);
    }

    [Fact]
    public void FailureResultIncludesErrorAndOmitsNulls()
    {
        var msg = ResultMessage.Failure("req-9", new ProtocolError(ErrorCode.Validation, "frames: 0〜300", false, new Dictionary<string, string> { ["frames"] = "0〜300" }));
        var json = JsonSerializer.Serialize(msg, msg.GetType(), Protocol.Json);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("result", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("validation", root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("0〜300", root.GetProperty("error").GetProperty("fieldErrors").GetProperty("frames").GetString());
        Assert.False(root.TryGetProperty("data", out _));
        Assert.False(root.TryGetProperty("revision", out _));
    }

    [Fact]
    public void ParsesReadyViewportAndCommandFromWeb()
    {
        var ready = JsonSerializer.Deserialize<WebMessage>("""{"protocolVersion":1,"type":"ready","clientVersion":"3.0.0","capabilities":["m1","m2a"]}""", Protocol.Json)!;
        Assert.Equal("ready", ready.Type);
        Assert.Equal(new[] { "m1", "m2a" }, ready.Capabilities);

        var viewport = JsonSerializer.Deserialize<WebMessage>("""{"protocolVersion":1,"type":"viewport","waveformWidth":960,"devicePixelRatio":2,"visible":true}""", Protocol.Json)!;
        Assert.Equal(960, viewport.WaveformWidth);
        Assert.True(viewport.Visible);

        var cmd = JsonSerializer.Deserialize<WebMessage>("""{"protocolVersion":1,"type":"command","requestId":"a1","command":"receive.setOffset","args":{"value":"+00:00:01:00"},"expectedRevision":3}""", Protocol.Json)!;
        Assert.Equal("receive.setOffset", cmd.Command);
        Assert.Equal(3, cmd.ExpectedRevision);
        Assert.Equal("+00:00:01:00", cmd.Args!.Value.GetProperty("value").GetString());
    }

    [Fact]
    public void SnapshotSerializesWithoutThrowing()
    {
        var h = new HostHarness();
        var msg = new SnapshotMessage(h.State.Revision, h.State.BuildSnapshot());
        var json = JsonSerializer.Serialize(msg, msg.GetType(), Protocol.Json);
        using var doc = JsonDocument.Parse(json);
        var state = doc.RootElement.GetProperty("state");
        Assert.Equal("ltc", state.GetProperty("mode").GetString());
        Assert.Equal(3, state.GetProperty("receive").GetProperty("devices").GetArrayLength());
        Assert.Equal("macos", state.GetProperty("uiCapabilities").GetProperty("platform").GetString());
        Assert.Equal("停止中", state.GetProperty("transport").GetProperty("statusText").GetString());
    }
}
