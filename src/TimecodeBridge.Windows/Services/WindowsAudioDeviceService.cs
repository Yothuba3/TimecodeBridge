using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Windows.Services;

/// <summary>
/// NAudio MMDeviceEnumerator によるデバイス列挙（Windows）。
/// 入力一覧にはキャプチャデバイスに加えてレンダーデバイスのループバック取り込みも並べ、
/// 出力一覧にはレンダーデバイスをそのまま返す（共有TimecodeViewModelの契約に合わせる）。
/// </summary>
public class WindowsAudioDeviceService : IAudioDeviceService
{
    public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var result = new List<AudioDeviceInfo>();

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                result.Add(new AudioDeviceInfo(device.ID, device.FriendlyName, IsLoopback: false));
            }

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                result.Add(new AudioDeviceInfo(device.ID, $"{device.FriendlyName} (Loopback)", IsLoopback: true));
            }

            return result;
        }
        catch (COMException ex)
        {
            Trace.TraceWarning($"キャプチャデバイスの列挙に失敗しました: {ex.Message}");
            return [];
        }
    }

    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var result = new List<AudioDeviceInfo>();

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                result.Add(new AudioDeviceInfo(device.ID, device.FriendlyName, IsLoopback: false));
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
