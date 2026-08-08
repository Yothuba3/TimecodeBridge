using TimecodeBridge.Models;
using TimecodeBridge.Services.Interfaces;

namespace TimecodeBridge.Services;

public class HostRegistry : IHostRegistry
{
    // _hosts はUIスレッド（追加・編集）とOSC送信ワーカー（列挙）の両方から触られる。
    // 変更は _gate で保護し、読み取りはスナップショットを返す。
    private readonly object _gate = new();
    private readonly List<OscHost> _hosts = [];

    public IReadOnlyList<OscHost> Hosts
    {
        get { lock (_gate) return _hosts.ToList().AsReadOnly(); }
    }

    public event EventHandler<HostChangedEventArgs>? HostChanged;

    public void AddHost(OscHost host)
    {
        lock (_gate)
        {
            if (_hosts.Any(h => h.Id == host.Id))
            {
                throw new ArgumentException($"Host with Id '{host.Id}' already exists.");
            }

            _hosts.Add(host);
        }
        HostChanged?.Invoke(this, new HostChangedEventArgs
        {
            HostId = host.Id,
            ChangeType = HostChangeType.Added,
        });
    }

    public void UpdateHost(string hostId, OscHost updatedHost)
    {
        lock (_gate)
        {
            var index = _hosts.FindIndex(h => h.Id == hostId);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Host with Id '{hostId}' not found.");
            }

            _hosts[index] = updatedHost;
        }
        HostChanged?.Invoke(this, new HostChangedEventArgs
        {
            HostId = hostId,
            ChangeType = HostChangeType.Updated,
        });
    }

    public void RemoveHost(string hostId)
    {
        lock (_gate)
        {
            var index = _hosts.FindIndex(h => h.Id == hostId);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Host with Id '{hostId}' not found.");
            }

            _hosts.RemoveAt(index);
        }
        HostChanged?.Invoke(this, new HostChangedEventArgs
        {
            HostId = hostId,
            ChangeType = HostChangeType.Removed,
        });
    }

    public void SetHostEnabled(string hostId, bool enabled)
    {
        OscHost? host;
        lock (_gate)
        {
            host = _hosts.FirstOrDefault(h => h.Id == hostId);
        }
        if (host is null)
        {
            throw new KeyNotFoundException($"Host with Id '{hostId}' not found.");
        }

        host.IsEnabled = enabled;
        HostChanged?.Invoke(this, new HostChangedEventArgs
        {
            HostId = hostId,
            ChangeType = HostChangeType.Updated,
        });
    }

    public IReadOnlyList<OscHost> GetEnabledHosts(IReadOnlyList<string> hostIds)
    {
        lock (_gate)
        {
            return _hosts
                .Where(h => hostIds.Contains(h.Id) && h.IsEnabled)
                .ToList()
                .AsReadOnly();
        }
    }
}
