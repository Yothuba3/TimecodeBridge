using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Core.Services.Interfaces;

public interface IOscSender
{
    void Send(string oscAddress, IReadOnlyList<OscArgument> arguments, IReadOnlyList<string> targetHostIds);
    OscDispatchResult SendWithResult(string oscAddress, IReadOnlyList<OscArgument> arguments, IReadOnlyList<string> targetHostIds)
    {
        Send(oscAddress, arguments, targetHostIds);
        return new OscDispatchResult(targetHostIds.Count, []);
    }
    void SendPing(string hostId);
    Task SendIcmpPingAsync(string hostId, int framesPerSecond);
    event EventHandler<OscSendResultEventArgs> SendCompleted;
}

public readonly record struct OscDispatchResult(int SentCount, IReadOnlyList<string> SkippedHostIds)
{
    public bool Sent => SentCount > 0;
}

public class OscSendResultEventArgs : EventArgs
{
    public required string OscAddress { get; init; }
    public required string HostId { get; init; }
    public required string HostName { get; init; }
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}
