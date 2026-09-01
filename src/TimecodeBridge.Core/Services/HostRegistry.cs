using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Core.Services;

public class HostRegistry : IHostRegistry
{
    private readonly List<OscHost> _hosts = [];

    public IReadOnlyList<OscHost> Hosts => _hosts.AsReadOnly();

    public event EventHandler<HostChangedEventArgs>? HostChanged;

    public void AddHost(OscHost host)
    {
        if (_hosts.Any(h => h.Id == host.Id))
        {
            throw new ArgumentException($"Host with Id '{host.Id}' already exists.");
        }

        _hosts.Add(host);
        HostChanged?.Invoke(this, new HostChangedEventArgs
        {
            HostId = host.Id,
            ChangeType = HostChangeType.Added,
        });
    }

    public void UpdateHost(string hostId, OscHost updatedHost)
    {
        var index = _hosts.FindIndex(h => h.Id == hostId);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Host with Id '{hostId}' not found.");
        }

        _hosts[index] = updatedHost;
        HostChanged?.Invoke(this, new HostChangedEventArgs
        {
            HostId = hostId,
            ChangeType = HostChangeType.Updated,
        });
    }

    public void RemoveHost(string hostId)
    {
        var index = _hosts.FindIndex(h => h.Id == hostId);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Host with Id '{hostId}' not found.");
        }

        _hosts.RemoveAt(index);
        HostChanged?.Invoke(this, new HostChangedEventArgs
        {
            HostId = hostId,
            ChangeType = HostChangeType.Removed,
        });
    }

    public void SetHostEnabled(string hostId, bool enabled)
    {
        var host = _hosts.FirstOrDefault(h => h.Id == hostId)
            ?? throw new KeyNotFoundException($"Host with Id '{hostId}' not found.");

        host.IsEnabled = enabled;
        HostChanged?.Invoke(this, new HostChangedEventArgs
        {
            HostId = hostId,
            ChangeType = HostChangeType.Updated,
        });
    }

    public IReadOnlyList<OscHost> GetEnabledHosts(IReadOnlyList<string> hostIds)
    {
        return _hosts
            .Where(h => hostIds.Contains(h.Id) && h.IsEnabled)
            .ToList()
            .AsReadOnly();
    }
}
