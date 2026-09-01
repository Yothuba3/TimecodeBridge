using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Core.Services.Interfaces;

/// <summary>
/// Abstracts the low-level UDP transport for OSC messages,
/// enabling unit testing without actual network I/O.
/// </summary>
public interface IOscTransport
{
    void Send(string ipAddress, int port, string oscAddress, IReadOnlyList<OscArgument> arguments);
}
