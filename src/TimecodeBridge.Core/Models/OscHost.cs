using System.Net;
using System.Net.Sockets;

namespace TimecodeBridge.Core.Models;

public class OscHost
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string IpAddress { get; set; }
    public required int Port { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// IPアドレス表記だけを受理する（ホスト名は不可）。
    /// <see cref="IPAddress.TryParse(string?, out IPAddress?)"/> は "192.168.1" や "1" のような
    /// 省略形も通すため、IPv4は正規表記と一致する場合のみ有効とする。
    /// </summary>
    public static bool TryParseIpAddress(string? text, out IPAddress address)
    {
        address = IPAddress.None;
        if (string.IsNullOrWhiteSpace(text)) return false;

        text = text.Trim();
        if (!IPAddress.TryParse(text, out var parsed)) return false;
        if (parsed.AddressFamily == AddressFamily.InterNetwork && parsed.ToString() != text) return false;

        address = parsed;
        return true;
    }
}
