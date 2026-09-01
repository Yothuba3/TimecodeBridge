using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Services;

/// <summary>
/// NAudio MMDeviceEnumerator を使用してオーディオデバイスを列挙するサービス
/// </summary>
public class AudioDeviceService : IAudioDeviceService
{
    /// <inheritdoc />
    public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            var result = new List<AudioDeviceInfo>();

            foreach (var device in devices)
            {
                result.Add(new AudioDeviceInfo(device.ID, device.FriendlyName, IsLoopback: false));
            }

            return result;
        }
        catch (COMException ex)
        {
            Trace.TraceWarning($"キャプチャデバイスの列挙に失敗しました: {ex.Message}");
            return [];
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            var result = new List<AudioDeviceInfo>();

            foreach (var device in renderDevices)
            {
                // ループバック用デバイス名に " (Loopback)" を付加
                result.Add(new AudioDeviceInfo(device.ID, $"{device.FriendlyName} (Loopback)", IsLoopback: true));
            }

            return result;
        }
        catch (COMException ex)
        {
            Trace.TraceWarning($"レンダーデバイスの列挙に失敗しました: {ex.Message}");
            return [];
        }
    }
}
