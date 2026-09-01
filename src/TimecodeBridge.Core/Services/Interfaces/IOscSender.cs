using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Core.Services.Interfaces;

public interface IOscSender
{
    void Send(string oscAddress, IReadOnlyList<OscArgument> arguments, IReadOnlyList<string> targetHostIds);
    void SendPing(string hostId);
    Task SendIcmpPingAsync(string hostId, int framesPerSecond);
    event EventHandler<OscSendResultEventArgs> SendCompleted;
}

public class OscSendResultEventArgs : EventArgs
{
    public required string OscAddress { get; init; }
    public required string HostId { get; init; }
    public required string HostName { get; init; }
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}
