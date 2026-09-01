using System.Net.NetworkInformation;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Core.Services;

public class OscSender : IOscSender
{
    private readonly IHostRegistry _hostRegistry;
    private readonly IOscTransport _transport;

    public event EventHandler<OscSendResultEventArgs>? SendCompleted;

    public OscSender(IHostRegistry hostRegistry, IOscTransport transport)
    {
        _hostRegistry = hostRegistry;
        _transport = transport;
    }

    public async Task SendIcmpPingAsync(string hostId, int framesPerSecond)
    {
        if (!TryGetHost(hostId, "/ping", out var host))
            return;

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host.IpAddress, 3000);

            if (reply.Status == IPStatus.Success)
            {
                var rttMs = reply.RoundtripTime;
                var frameDurationMs = 1000.0 / framesPerSecond;
                var delayFrames = rttMs / frameDurationMs;

                NotifyResult(host, "/ping", true, $"RTT: {rttMs}ms ({delayFrames:F1} frames @ {framesPerSecond}fps)");
            }
            else
            {
                NotifyResult(host, "/ping", false, $"Ping failed: {reply.Status}");
            }
        }
        catch (Exception ex)
        {
            NotifyResult(host, "/ping", false, $"Ping error: {ex.Message}");
        }
    }

    public void Send(string oscAddress, IReadOnlyList<OscArgument> arguments, IReadOnlyList<string> targetHostIds)
    {
        var enabledHosts = _hostRegistry.GetEnabledHosts(targetHostIds);

        foreach (var host in enabledHosts)
        {
            SendToHost(host, oscAddress, arguments);
        }
    }

    public void SendPing(string hostId)
    {
        if (!TryGetHost(hostId, "/ping", out var host))
            return;

        SendToHost(host, "/ping", []);
    }

    private bool TryGetHost(string hostId, string oscAddress, out OscHost host)
    {
        host = _hostRegistry.Hosts.FirstOrDefault(h => h.Id == hostId)!;
        if (host is not null)
            return true;

        SendCompleted?.Invoke(this, new OscSendResultEventArgs
        {
            OscAddress = oscAddress,
            HostId = hostId,
            HostName = string.Empty,
            Success = false,
            ErrorMessage = $"Host with Id '{hostId}' not found.",
        });
        return false;
    }

    private void SendToHost(OscHost host, string oscAddress, IReadOnlyList<OscArgument> arguments)
    {
        try
        {
            _transport.Send(host.IpAddress, host.Port, oscAddress, arguments);
            NotifyResult(host, oscAddress, true);
        }
        catch (Exception ex)
        {
            NotifyResult(host, oscAddress, false, ex.Message);
        }
    }

    private void NotifyResult(OscHost host, string oscAddress, bool success, string? errorMessage = null)
    {
        SendCompleted?.Invoke(this, new OscSendResultEventArgs
        {
            OscAddress = oscAddress,
            HostId = host.Id,
            HostName = host.Name,
            Success = success,
            ErrorMessage = errorMessage,
        });
    }
}
