using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using TimecodeBridge.Host.Bridge;
using TimecodeBridge.Host.Services;
using Xunit;

namespace TimecodeBridge.Host.Tests;

public class DevAutomationTests
{
    private sealed class FakeTarget : IAutomationTarget
    {
        public bool WebReady { get; set; } = true;
        public SnapshotMessage? Snapshot { get; set; }
        public WebMessage? LastCommand { get; private set; }
        public string? LastScript { get; private set; }
        public byte[]? Png { get; set; }
        public int Activated { get; private set; }
        public int Reloaded { get; private set; }

        public Task<SnapshotMessage> SnapshotAsync() => Task.FromResult(Snapshot!);
        public Task<ClockState> ClockAsync() => Task.FromResult(new ClockState("01:02:03:04", "01:02:03:04", 0, "30", false, 0));
        public Task<ResultMessage> ExecuteAsync(WebMessage message)
        {
            LastCommand = message;
            return Task.FromResult(ResultMessage.Success(message.RequestId!, new { echoed = message.Command }, 7));
        }
        public Task<string?> EvalAsync(string script) { LastScript = script; return Task.FromResult<string?>("42"); }
        public Task<byte[]?> CaptureWindowAsync() => Task.FromResult(Png);
        public Task ActivateAsync() { Activated++; return Task.CompletedTask; }
        public Task ReloadWebAsync() { Reloaded++; return Task.CompletedTask; }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static (DevAutomation Server, FakeTarget Target, HttpClient Client) Start()
    {
        var target = new FakeTarget();
        var server = new DevAutomation(target, FreePort());
        return (server, target, new HttpClient { BaseAddress = new Uri(server.BaseUrl) });
    }

    [Fact]
    public async Task HealthReportsWebReady()
    {
        var (server, target, client) = Start();
        using (server)
        {
            var health = await client.GetFromJsonAsync<JsonElement>("health", Ct);
            Assert.True(health.GetProperty("ok").GetBoolean());
            Assert.True(health.GetProperty("webReady").GetBoolean());
            target.WebReady = false;
            health = await client.GetFromJsonAsync<JsonElement>("health", Ct);
            Assert.False(health.GetProperty("webReady").GetBoolean());
        }
    }

    [Fact]
    public async Task CommandIsForwardedAsWebMessage()
    {
        var (server, target, client) = Start();
        using (server)
        {
            var res = await client.PostAsync("command", new StringContent("""{"command":"cue.fire","args":{"id":"c1"},"expectedRevision":3}""", Encoding.UTF8, "application/json"), Ct);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = await res.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.True(body.GetProperty("ok").GetBoolean());
            Assert.Equal("cue.fire", body.GetProperty("data").GetProperty("echoed").GetString());
            Assert.Equal(7, body.GetProperty("revision").GetInt64());

            var msg = target.LastCommand!;
            Assert.Equal("command", msg.Type);
            Assert.Equal(Protocol.Version, msg.ProtocolVersion);
            Assert.Equal("cue.fire", msg.Command);
            Assert.Equal("c1", msg.Args!.Value.GetProperty("id").GetString());
            Assert.Equal(3, msg.ExpectedRevision);
            Assert.StartsWith("auto-", msg.RequestId);
        }
    }

    [Fact]
    public async Task CommandWithoutNameIsBadRequest()
    {
        var (server, target, client) = Start();
        using (server)
        {
            var res = await client.PostAsync("command", new StringContent("""{"args":{}}""", Encoding.UTF8, "application/json"), Ct);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Null(target.LastCommand);
            res = await client.PostAsync("command", new StringContent("not json", Encoding.UTF8, "application/json"), Ct);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }
    }

    [Fact]
    public async Task EvalPassesScriptThroughAndReturnsResult()
    {
        var (server, target, client) = Start();
        using (server)
        {
            var res = await client.PostAsync("eval", new StringContent("document.title", Encoding.UTF8, "text/plain"), Ct);
            var body = await res.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("42", body.GetProperty("result").GetString());
            Assert.Equal("document.title", target.LastScript);

            res = await client.PostAsync("eval", new StringContent("  ", Encoding.UTF8, "text/plain"), Ct);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }
    }

    [Fact]
    public async Task ScreenshotReturnsPngOrNotImplemented()
    {
        var (server, target, client) = Start();
        using (server)
        {
            var res = await client.GetAsync("screenshot", Ct);
            Assert.Equal(HttpStatusCode.NotImplemented, res.StatusCode);

            target.Png = [0x89, 0x50, 0x4E, 0x47];
            res = await client.GetAsync("screenshot", Ct);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal("image/png", res.Content.Headers.ContentType!.MediaType);
            Assert.Equal(target.Png, await res.Content.ReadAsByteArrayAsync(Ct));
        }
    }

    [Fact]
    public async Task ActivateBringsWindowToFront()
    {
        var (server, target, client) = Start();
        using (server)
        {
            var res = await client.PostAsync("activate", new StringContent(""), Ct);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal(1, target.Activated);
        }
    }

    [Fact]
    public async Task ReloadNavigatesTheWebView()
    {
        var (server, target, client) = Start();
        using (server)
        {
            var res = await client.PostAsync("reload", new StringContent(""), Ct);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal(1, target.Reloaded);
        }
    }

    [Fact]
    public async Task UnknownRouteIsNotFound()
    {
        var (server, _, client) = Start();
        using (server)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("nothing", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("state", new StringContent(""), Ct)).StatusCode);
        }
    }

    [AvaloniaFact]
    public void StateAndClockAreSerializedLikeTheBridge()
    {
        var h = new HostHarness();
        var (server, target, client) = Start();
        target.Snapshot = new SnapshotMessage(h.State.Revision, h.State.BuildSnapshot());
        using (server)
        {
            var state = client.GetFromJsonAsync<JsonElement>("state", Ct).GetAwaiter().GetResult();
            Assert.Equal("snapshot", state.GetProperty("type").GetString());
            Assert.Equal(h.State.SessionId, state.GetProperty("state").GetProperty("sessionId").GetString());
            Assert.Equal("ltc", state.GetProperty("state").GetProperty("mode").GetString());

            var clock = client.GetFromJsonAsync<JsonElement>("clock", Ct).GetAwaiter().GetResult();
            Assert.Equal("01:02:03:04", clock.GetProperty("raw").GetString());
        }
    }
}
