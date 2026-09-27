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
        => SendWithResult(oscAddress, arguments, targetHostIds);

    public OscDispatchResult SendWithResult(string oscAddress, IReadOnlyList<OscArgument> arguments, IReadOnlyList<string> targetHostIds)
    {
        var hostsById = _hostRegistry.Hosts.ToDictionary(h => h.Id);
        var enabledHosts = new List<OscHost>();
        var skipped = new List<string>();
        var disabledNames = new List<string>();
        var missingIds = new List<string>();
        foreach (var id in targetHostIds.Distinct())
        {
            if (!hostsById.TryGetValue(id, out var host)) { skipped.Add(id); missingIds.Add(id); }
            else if (!host.IsEnabled) { skipped.Add(id); disabledNames.Add(host.Name); }
            else enabledHosts.Add(host);
        }

        var sentCount = 0;
        foreach (var host in enabledHosts)
        {
            if (SendToHost(host, oscAddress, arguments)) sentCount++;
        }

        if (enabledHosts.Count == 0)
        {
            var details = new List<string>();
            if (disabledNames.Count > 0) details.Add($"無効: {string.Join(", ", disabledNames)}");
            if (missingIds.Count > 0) details.Add($"見つからない: {string.Join(", ", missingIds)}");
            SendCompleted?.Invoke(this, new OscSendResultEventArgs
            {
                OscAddress = oscAddress, HostId = string.Empty, HostName = string.Empty, Success = false,
                ErrorMessage = details.Count == 0 ? "送信先がありません" : $"送信先がありません({string.Join(", ", details)})",
            });
        }
        return new OscDispatchResult(sentCount, skipped);
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

    private bool SendToHost(OscHost host, string oscAddress, IReadOnlyList<OscArgument> arguments)
    {
        try
        {
            _transport.Send(host.IpAddress, host.Port, oscAddress, arguments);
            NotifyResult(host, oscAddress, true);
            return true;
        }
        catch (Exception ex)
        {
            NotifyResult(host, oscAddress, false, ex.Message);
            return false;
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
