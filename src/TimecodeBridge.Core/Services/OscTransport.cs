using BuildSoft.OscCore;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Core.Services;

/// <summary>
/// Production implementation of IOscTransport using BuildSoft.OscCore's OscClient for UDP sending.
/// </summary>
public class OscTransport : IOscTransport
{
    public void Send(string ipAddress, int port, string oscAddress, IReadOnlyList<OscArgument> arguments)
    {
        using var client = new OscClient(ipAddress, port);

        if (arguments.Count == 0)
        {
            client.Send(oscAddress);
            return;
        }

        if (arguments.Count == 1)
        {
            SendSingleArgument(client, oscAddress, arguments[0]);
            return;
        }

        // For multiple arguments, send each as a separate single-argument message
        // since OscCore does not provide a built-in multi-argument send API.
        // If multi-argument messages are needed, use OscWriter directly.
        foreach (var arg in arguments)
        {
            SendSingleArgument(client, oscAddress, arg);
        }
    }

    private static void SendSingleArgument(OscClient client, string oscAddress, OscArgument argument)
    {
        switch (argument)
        {
            case OscInt32Argument intArg:
                client.Send(oscAddress, intArg.Value);
                break;
            case OscFloat32Argument floatArg:
                client.Send(oscAddress, floatArg.Value);
                break;
            case OscStringArgument stringArg:
                client.Send(oscAddress, stringArg.Value);
                break;
            default:
                throw new ArgumentException($"Unsupported OscArgument type: {argument.GetType().Name}");
        }
    }
}
