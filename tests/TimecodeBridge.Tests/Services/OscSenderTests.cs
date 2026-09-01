using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
namespace TimecodeBridge.Tests.Services;

using TimecodeBridge.Core.Models;
using TimecodeBridge.Services;
using TimecodeBridge.Services.Interfaces;

// --- Test Doubles ---

/// <summary>
/// Stub IHostRegistry that returns preconfigured hosts.
/// </summary>
internal class StubHostRegistry : IHostRegistry
{
    private readonly List<OscHost> _hosts = [];

    public IReadOnlyList<OscHost> Hosts => _hosts.AsReadOnly();

    public event EventHandler<HostChangedEventArgs>? HostChanged;

    public void AddHost(OscHost host) => _hosts.Add(host);
    public void UpdateHost(string hostId, OscHost updatedHost) { }
    public void RemoveHost(string hostId) { }
    public void SetHostEnabled(string hostId, bool enabled) { }

    public IReadOnlyList<OscHost> GetEnabledHosts(IReadOnlyList<string> hostIds)
    {
        return _hosts
            .Where(h => hostIds.Contains(h.Id) && h.IsEnabled)
            .ToList()
            .AsReadOnly();
    }

    // Suppress unused event warning
    internal void RaiseHostChanged() => HostChanged?.Invoke(this, null!);
}

/// <summary>
/// Spy IOscTransport that records all send calls and can be configured to throw.
/// </summary>
internal class SpyOscTransport : IOscTransport
{
    public record SendCall(string IpAddress, int Port, string OscAddress, IReadOnlyList<OscArgument> Arguments);

    public List<SendCall> Calls { get; } = [];
    public Exception? ExceptionToThrow { get; set; }
    public Dictionary<string, Exception> ExceptionsByIp { get; } = [];

    public void Send(string ipAddress, int port, string oscAddress, IReadOnlyList<OscArgument> arguments)
    {
        Calls.Add(new SendCall(ipAddress, port, oscAddress, arguments));

        if (ExceptionsByIp.TryGetValue(ipAddress, out var ex))
        {
            throw ex;
        }

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }
    }
}

// --- Tests ---

public class OscSenderTests
{
    private readonly StubHostRegistry _hostRegistry = new();
    private readonly SpyOscTransport _transport = new();
    private readonly OscSender _sender;

    public OscSenderTests()
    {
        _sender = new OscSender(_hostRegistry, _transport);
    }

    private static OscHost CreateHost(string id = "host-1", string name = "Test Host",
        string ip = "192.168.1.100", int port = 8000, bool enabled = true)
    {
        return new OscHost
        {
            Id = id,
            Name = name,
            IpAddress = ip,
            Port = port,
            IsEnabled = enabled,
        };
    }

    // --- Send: basic routing ---

    [Fact]
    public void Send_SendsToEnabledHostsOnly()
    {
        _hostRegistry.AddHost(CreateHost("h1", "Host1", "10.0.0.1", 8001, enabled: true));
        _hostRegistry.AddHost(CreateHost("h2", "Host2", "10.0.0.2", 8002, enabled: false));
        _hostRegistry.AddHost(CreateHost("h3", "Host3", "10.0.0.3", 8003, enabled: true));

        _sender.Send("/test", [], ["h1", "h2", "h3"]);

        Assert.Equal(2, _transport.Calls.Count);
        Assert.Equal("10.0.0.1", _transport.Calls[0].IpAddress);
        Assert.Equal(8001, _transport.Calls[0].Port);
        Assert.Equal("10.0.0.3", _transport.Calls[1].IpAddress);
        Assert.Equal(8003, _transport.Calls[1].Port);
    }

    [Fact]
    public void Send_PassesOscAddressToTransport()
    {
        _hostRegistry.AddHost(CreateHost("h1"));

        _sender.Send("/my/address", [], ["h1"]);

        Assert.Single(_transport.Calls);
        Assert.Equal("/my/address", _transport.Calls[0].OscAddress);
    }

    [Fact]
    public void Send_PassesArgumentsToTransport()
    {
        _hostRegistry.AddHost(CreateHost("h1"));

        var args = new OscArgument[]
        {
            new OscInt32Argument(42),
            new OscFloat32Argument(3.14f),
            new OscStringArgument("hello"),
        };

        _sender.Send("/test", args, ["h1"]);

        Assert.Single(_transport.Calls);
        Assert.Equal(3, _transport.Calls[0].Arguments.Count);
    }

    [Fact]
    public void Send_NoMatchingHosts_DoesNotSend()
    {
        _hostRegistry.AddHost(CreateHost("h1", enabled: false));

        _sender.Send("/test", [], ["h1"]);

        Assert.Empty(_transport.Calls);
    }

    [Fact]
    public void Send_EmptyTargetHostIds_DoesNotSend()
    {
        _hostRegistry.AddHost(CreateHost("h1"));

        _sender.Send("/test", [], []);

        Assert.Empty(_transport.Calls);
    }

    // --- Send: SendCompleted event on success ---

    [Fact]
    public void Send_RaisesSendCompletedForEachHost()
    {
        _hostRegistry.AddHost(CreateHost("h1", "Host1", "10.0.0.1", 8001));
        _hostRegistry.AddHost(CreateHost("h2", "Host2", "10.0.0.2", 8002));

        var results = new List<OscSendResultEventArgs>();
        _sender.SendCompleted += (_, args) => results.Add(args);

        _sender.Send("/test", [], ["h1", "h2"]);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal("h1", results[0].HostId);
        Assert.Equal("Host1", results[0].HostName);
        Assert.Equal("/test", results[0].OscAddress);
        Assert.Equal("h2", results[1].HostId);
        Assert.Equal("Host2", results[1].HostName);
    }

    // --- Send: SendCompleted event on failure ---

    [Fact]
    public void Send_TransportThrows_RaisesSendCompletedWithError()
    {
        _hostRegistry.AddHost(CreateHost("h1", "Host1", "10.0.0.1", 8001));
        _transport.ExceptionToThrow = new InvalidOperationException("Network error");

        var results = new List<OscSendResultEventArgs>();
        _sender.SendCompleted += (_, args) => results.Add(args);

        _sender.Send("/test", [], ["h1"]);

        Assert.Single(results);
        Assert.False(results[0].Success);
        Assert.Equal("h1", results[0].HostId);
        Assert.Contains("Network error", results[0].ErrorMessage);
    }

    [Fact]
    public void Send_PartialFailure_ContinuesSendingToOtherHosts()
    {
        _hostRegistry.AddHost(CreateHost("h1", "Host1", "10.0.0.1", 8001));
        _hostRegistry.AddHost(CreateHost("h2", "Host2", "10.0.0.2", 8002));
        _transport.ExceptionsByIp["10.0.0.1"] = new InvalidOperationException("fail");

        var results = new List<OscSendResultEventArgs>();
        _sender.SendCompleted += (_, args) => results.Add(args);

        _sender.Send("/test", [], ["h1", "h2"]);

        Assert.Equal(2, results.Count);
        Assert.False(results[0].Success);
        Assert.True(results[1].Success);
    }

    // --- SendPing ---

    [Fact]
    public void SendPing_SendsPingMessageToSpecifiedHost()
    {
        _hostRegistry.AddHost(CreateHost("h1", "Host1", "10.0.0.1", 8001));

        _sender.SendPing("h1");

        Assert.Single(_transport.Calls);
        Assert.Equal("/ping", _transport.Calls[0].OscAddress);
        Assert.Equal("10.0.0.1", _transport.Calls[0].IpAddress);
        Assert.Equal(8001, _transport.Calls[0].Port);
    }

    [Fact]
    public void SendPing_RaisesSendCompletedOnSuccess()
    {
        _hostRegistry.AddHost(CreateHost("h1", "Host1", "10.0.0.1", 8001));

        var results = new List<OscSendResultEventArgs>();
        _sender.SendCompleted += (_, args) => results.Add(args);

        _sender.SendPing("h1");

        Assert.Single(results);
        Assert.True(results[0].Success);
        Assert.Equal("h1", results[0].HostId);
        Assert.Equal("/ping", results[0].OscAddress);
    }

    [Fact]
    public void SendPing_TransportThrows_RaisesSendCompletedWithError()
    {
        _hostRegistry.AddHost(CreateHost("h1", "Host1", "10.0.0.1", 8001));
        _transport.ExceptionToThrow = new InvalidOperationException("Connection refused");

        var results = new List<OscSendResultEventArgs>();
        _sender.SendCompleted += (_, args) => results.Add(args);

        _sender.SendPing("h1");

        Assert.Single(results);
        Assert.False(results[0].Success);
        Assert.Contains("Connection refused", results[0].ErrorMessage);
    }

    [Fact]
    public void SendPing_UnknownHostId_RaisesSendCompletedWithError()
    {
        var results = new List<OscSendResultEventArgs>();
        _sender.SendCompleted += (_, args) => results.Add(args);

        _sender.SendPing("nonexistent");

        Assert.Single(results);
        Assert.False(results[0].Success);
        Assert.Equal("nonexistent", results[0].HostId);
    }

    [Fact]
    public void SendPing_DisabledHost_StillSendsPing()
    {
        // Ping is a connection test, so it should work even for disabled hosts
        _hostRegistry.AddHost(CreateHost("h1", "Host1", "10.0.0.1", 8001, enabled: false));

        _sender.SendPing("h1");

        Assert.Single(_transport.Calls);
    }
}
