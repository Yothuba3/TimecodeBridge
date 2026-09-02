using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Core.Services;

/// <summary>
/// Production implementation of IOscTransport.
/// OSCメッセージを自前でエンコードしUDPで送出する。複数引数も1つのメッセージにまとめて送る
/// （旧実装の BuildSoft.OscCore は複数引数メッセージを組めず、引数ごとに別送していた）。
/// 宛先はIPアドレス表記のみ。名前解決はタイムコード処理スレッド上で数秒ブロックし得るため行わない。
/// </summary>
public class OscTransport : IOscTransport
{
    public void Send(string ipAddress, int port, string oscAddress, IReadOnlyList<OscArgument> arguments)
    {
        if (!OscHost.TryParseIpAddress(ipAddress, out var address))
            throw new ArgumentException($"IPアドレスではありません: {ipAddress}");

        var payload = EncodeMessage(oscAddress, arguments);
        using var udp = new UdpClient(address.AddressFamily);
        if (address.AddressFamily == AddressFamily.InterNetwork)
            udp.EnableBroadcast = true; // ブロードキャストアドレス宛の送信を許可
        udp.Send(payload, payload.Length, new IPEndPoint(address, port));
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
