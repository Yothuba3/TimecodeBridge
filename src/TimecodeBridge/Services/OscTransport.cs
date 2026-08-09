using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TimecodeBridge.Models;
using TimecodeBridge.Services.Interfaces;

namespace TimecodeBridge.Services;

/// <summary>
/// Production implementation of IOscTransport.
/// OSCメッセージを自前でエンコードしUDPで送出する。複数引数も1つのメッセージにまとめて送る
/// （旧実装の BuildSoft.OscCore は複数引数メッセージを組めず、引数ごとに別送していた）。
/// </summary>
public class OscTransport : IOscTransport
{
    // ponytail: ホスト名の初回解決は同期ブロックが残る（失敗も30秒キャッシュして毎フレームの
    // DNSタイムアウトでタイムコード処理が止まるのを防ぐ）。完全非同期化が必要になったら送信キュー化
    private static readonly ConcurrentDictionary<string, (IPAddress? Address, DateTime Expires)> _dnsCache = new();
    private static readonly TimeSpan DnsCacheTtl = TimeSpan.FromSeconds(30);

    public void Send(string ipAddress, int port, string oscAddress, IReadOnlyList<OscArgument> arguments)
    {
        var address = ResolveCached(ipAddress)
            ?? throw new InvalidOperationException($"ホスト名を解決できません: {ipAddress}");

        var payload = EncodeMessage(oscAddress, arguments);
        using var udp = new UdpClient();
        udp.EnableBroadcast = true; // ブロードキャストアドレス宛の送信を許可
        udp.Send(payload, payload.Length, new IPEndPoint(address, port));
    }

    private static IPAddress? ResolveCached(string host)
    {
        if (IPAddress.TryParse(host, out var literal)) return literal;

        if (_dnsCache.TryGetValue(host, out var cached) && cached.Expires > DateTime.UtcNow)
            return cached.Address;

        IPAddress? resolved = null;
        try
        {
            var addresses = Dns.GetHostAddresses(host);
            resolved = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                ?? addresses.FirstOrDefault();
        }
        catch
        {
            // 解決失敗もキャッシュして連続タイムアウトを防ぐ
        }

        _dnsCache[host] = (resolved, DateTime.UtcNow + DnsCacheTtl);
        return resolved;
    }

    /// <summary>OSC 1.0 メッセージ（アドレス + タイプタグ + 引数、各4バイト境界）へエンコードする。</summary>
    public static byte[] EncodeMessage(string oscAddress, IReadOnlyList<OscArgument> arguments)
    {
        using var ms = new MemoryStream();
        WritePaddedString(ms, oscAddress);

        var typeTags = new StringBuilder(",", arguments.Count + 1);
        foreach (var arg in arguments)
        {
            typeTags.Append(arg switch
            {
                OscInt32Argument => 'i',
                OscFloat32Argument => 'f',
                OscStringArgument => 's',
                _ => throw new ArgumentException($"Unsupported OscArgument type: {arg.GetType().Name}"),
            });
        }
        WritePaddedString(ms, typeTags.ToString());

        foreach (var arg in arguments)
        {
            switch (arg)
            {
                case OscInt32Argument intArg:
                    WriteBigEndianInt32(ms, intArg.Value);
                    break;
                case OscFloat32Argument floatArg:
                    WriteBigEndianInt32(ms, BitConverter.SingleToInt32Bits(floatArg.Value));
                    break;
                case OscStringArgument stringArg:
                    WritePaddedString(ms, stringArg.Value);
                    break;
            }
        }

        return ms.ToArray();
    }

    // OSC文字列はnull終端し、全体を4バイト境界へパディングする（最低1つのnullを含む）
    private static void WritePaddedString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
        int pad = 4 - (bytes.Length % 4);
        stream.Write(new byte[pad], 0, pad);
    }

    private static void WriteBigEndianInt32(Stream stream, int value)
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buf, value);
        stream.Write(buf, 0, 4);
    }
}
