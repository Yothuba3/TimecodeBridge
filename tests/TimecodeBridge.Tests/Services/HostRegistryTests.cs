using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
namespace TimecodeBridge.Tests.Services;

using TimecodeBridge.Core.Models;
using TimecodeBridge.Services;
using TimecodeBridge.Services.Interfaces;

public class HostRegistryTests
{
    private readonly HostRegistry _registry = new();

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

    // --- AddHost ---

    [Fact]
    public void AddHost_AddsToHostsList()
    {
        var host = CreateHost();
        _registry.AddHost(host);

        Assert.Single(_registry.Hosts);
        Assert.Equal("host-1", _registry.Hosts[0].Id);
    }

    [Fact]
    public void AddHost_MultipleHosts_AllAppearInList()
    {
        _registry.AddHost(CreateHost("h1"));
        _registry.AddHost(CreateHost("h2"));
        _registry.AddHost(CreateHost("h3"));

        Assert.Equal(3, _registry.Hosts.Count);
    }

    [Fact]
    public void AddHost_DuplicateId_ThrowsArgumentException()
    {
        _registry.AddHost(CreateHost("h1"));

        Assert.Throws<ArgumentException>(() => _registry.AddHost(CreateHost("h1")));
    }

    [Fact]
    public void AddHost_RaisesHostChangedEvent()
    {
        HostChangedEventArgs? eventArgs = null;
        _registry.HostChanged += (_, args) => eventArgs = args;

        var host = CreateHost();
        _registry.AddHost(host);

        Assert.NotNull(eventArgs);
        Assert.Equal(HostChangeType.Added, eventArgs.ChangeType);
        Assert.Equal("host-1", eventArgs.HostId);
    }

    // --- UpdateHost ---

    [Fact]
    public void UpdateHost_UpdatesExistingHost()
    {
        _registry.AddHost(CreateHost("h1", "Old Name"));
        var updated = CreateHost("h1", "New Name", "10.0.0.1", 9000);

        _registry.UpdateHost("h1", updated);

        Assert.Equal("New Name", _registry.Hosts[0].Name);
        Assert.Equal("10.0.0.1", _registry.Hosts[0].IpAddress);
        Assert.Equal(9000, _registry.Hosts[0].Port);
    }

    [Fact]
    public void UpdateHost_NonExistentId_ThrowsKeyNotFoundException()
    {
        Assert.Throws<KeyNotFoundException>(() =>
            _registry.UpdateHost("nonexistent", CreateHost()));
    }

    [Fact]
    public void UpdateHost_RaisesHostChangedEvent()
    {
        _registry.AddHost(CreateHost("h1"));

        HostChangedEventArgs? eventArgs = null;
        _registry.HostChanged += (_, args) => eventArgs = args;

        _registry.UpdateHost("h1", CreateHost("h1", "Updated"));

        Assert.NotNull(eventArgs);
        Assert.Equal(HostChangeType.Updated, eventArgs.ChangeType);
        Assert.Equal("h1", eventArgs.HostId);
    }

    // --- RemoveHost ---

    [Fact]
    public void RemoveHost_RemovesFromList()
    {
        _registry.AddHost(CreateHost("h1"));
        _registry.AddHost(CreateHost("h2"));

        _registry.RemoveHost("h1");

        Assert.Single(_registry.Hosts);
        Assert.Equal("h2", _registry.Hosts[0].Id);
    }

    [Fact]
    public void RemoveHost_NonExistentId_ThrowsKeyNotFoundException()
    {
        Assert.Throws<KeyNotFoundException>(() => _registry.RemoveHost("nonexistent"));
    }

    [Fact]
    public void RemoveHost_RaisesHostChangedEvent()
    {
        _registry.AddHost(CreateHost("h1"));

        HostChangedEventArgs? eventArgs = null;
        _registry.HostChanged += (_, args) => eventArgs = args;

        _registry.RemoveHost("h1");

        Assert.NotNull(eventArgs);
        Assert.Equal(HostChangeType.Removed, eventArgs.ChangeType);
        Assert.Equal("h1", eventArgs.HostId);
    }

    // --- SetHostEnabled ---

    [Fact]
    public void SetHostEnabled_TogglesEnabledState()
    {
        _registry.AddHost(CreateHost("h1", enabled: true));

        _registry.SetHostEnabled("h1", false);
        Assert.False(_registry.Hosts[0].IsEnabled);

        _registry.SetHostEnabled("h1", true);
        Assert.True(_registry.Hosts[0].IsEnabled);
    }

    [Fact]
    public void SetHostEnabled_NonExistentId_ThrowsKeyNotFoundException()
    {
        Assert.Throws<KeyNotFoundException>(() => _registry.SetHostEnabled("nonexistent", false));
    }

    [Fact]
    public void SetHostEnabled_RaisesHostChangedEvent()
    {
        _registry.AddHost(CreateHost("h1"));

        HostChangedEventArgs? eventArgs = null;
        _registry.HostChanged += (_, args) => eventArgs = args;

        _registry.SetHostEnabled("h1", false);

        Assert.NotNull(eventArgs);
        Assert.Equal(HostChangeType.Updated, eventArgs.ChangeType);
        Assert.Equal("h1", eventArgs.HostId);
    }

    // --- GetEnabledHosts ---

    [Fact]
    public void GetEnabledHosts_ReturnsOnlyEnabledHostsFromIdList()
    {
        _registry.AddHost(CreateHost("h1", enabled: true));
        _registry.AddHost(CreateHost("h2", enabled: false));
        _registry.AddHost(CreateHost("h3", enabled: true));

        var result = _registry.GetEnabledHosts(["h1", "h2", "h3"]);

        Assert.Equal(2, result.Count);
        Assert.Equal("h1", result[0].Id);
        Assert.Equal("h3", result[1].Id);
    }

    [Fact]
    public void GetEnabledHosts_IgnoresUnknownIds()
    {
        _registry.AddHost(CreateHost("h1", enabled: true));

        var result = _registry.GetEnabledHosts(["h1", "unknown"]);

        Assert.Single(result);
        Assert.Equal("h1", result[0].Id);
    }

    [Fact]
    public void GetEnabledHosts_EmptyList_ReturnsEmpty()
    {
        _registry.AddHost(CreateHost("h1"));

        var result = _registry.GetEnabledHosts([]);

        Assert.Empty(result);
    }

    // --- Hosts は読み取り専用 ---

    [Fact]
    public void Hosts_ReturnsReadOnlyList()
    {
        _registry.AddHost(CreateHost("h1"));

        var hosts = _registry.Hosts;

        // IReadOnlyList であることを確認（外部から変更できない）
        Assert.IsAssignableFrom<IReadOnlyList<OscHost>>(hosts);
    }
}
