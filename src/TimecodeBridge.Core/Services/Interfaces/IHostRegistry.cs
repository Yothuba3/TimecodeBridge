using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Core.Services.Interfaces;

public interface IHostRegistry
{
    IReadOnlyList<OscHost> Hosts { get; }

    void AddHost(OscHost host);
    void UpdateHost(string hostId, OscHost updatedHost);
    void RemoveHost(string hostId);
    void SetHostEnabled(string hostId, bool enabled);
    IReadOnlyList<OscHost> GetEnabledHosts(IReadOnlyList<string> hostIds);

    event EventHandler<HostChangedEventArgs> HostChanged;
}

public enum HostChangeType
{
    Added,
    Updated,
    Removed,
}

public class HostChangedEventArgs : EventArgs
{
    public required string HostId { get; init; }
    public required HostChangeType ChangeType { get; init; }
}
