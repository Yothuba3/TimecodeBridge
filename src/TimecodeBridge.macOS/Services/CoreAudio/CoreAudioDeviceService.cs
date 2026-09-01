using System.Runtime.InteropServices;
using System.Text;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.macOS.Services.CoreAudio;

/// <summary>
/// CoreAudioを使用したオーディオデバイスサービス（本実装）
/// </summary>
public class CoreAudioDeviceService : IAudioDeviceService
{
    /// <summary>
    /// キャプチャ（入力）デバイスの一覧を取得
    /// </summary>
    public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices()
    {
        return GetDevices(CoreAudioInterop.kAudioObjectPropertyScopeInput);
    }

    /// <summary>
    /// レンダー（出力）デバイスの一覧を取得
    /// </summary>
    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
    {
        return GetDevices(CoreAudioInterop.kAudioObjectPropertyScopeOutput);
    }

    /// <summary>
    /// 指定されたスコープ（Input/Output）のデバイスリストを取得
    /// </summary>
    private IReadOnlyList<AudioDeviceInfo> GetDevices(uint scope)
    {
        var devices = new List<AudioDeviceInfo>();

        try
        {
            // デバイスリストの取得
            uint[] deviceIds = GetAudioDeviceIds();

            foreach (uint deviceId in deviceIds)
            {
                // デバイスが指定されたスコープのストリームを持つかチェック
                if (DeviceHasStreams(deviceId, scope))
                {
                    string deviceName = GetDeviceName(deviceId);
                    bool isLoopback = scope == CoreAudioInterop.kAudioObjectPropertyScopeOutput
                                      && deviceName.Contains("loopback", StringComparison.OrdinalIgnoreCase);

                    devices.Add(new AudioDeviceInfo(
                        deviceId.ToString(),
                        deviceName,
                        isLoopback
                    ));
                }
            }
        }
        catch (Exception ex)
        {
            // デバイス列挙失敗時はダミーデバイスを返す
            string scopeName = scope == CoreAudioInterop.kAudioObjectPropertyScopeInput ? "Input" : "Output";
            devices.Add(new AudioDeviceInfo(
                "error",
                $"Error enumerating devices: {ex.Message}",
                false
            ));
        }

        // デバイスが見つからない場合はデフォルトメッセージ
        if (devices.Count == 0)
        {
            string scopeName = scope == CoreAudioInterop.kAudioObjectPropertyScopeInput ? "Input" : "Output";
            devices.Add(new AudioDeviceInfo(
                "none",
                $"No {scopeName} devices found",
                false
            ));
        }

        return devices;
    }

    /// <summary>
    /// システム内の全オーディオデバイスIDを取得
    /// </summary>
    private uint[] GetAudioDeviceIds()
    {
        var address = new CoreAudioInterop.AudioObjectPropertyAddress
        {
            Selector = CoreAudioInterop.kAudioHardwarePropertyDevices,
            Scope = CoreAudioInterop.kAudioObjectPropertyScopeGlobal,
            Element = CoreAudioInterop.kAudioObjectPropertyElementMain
        };

        // プロパティサイズの取得
        uint dataSize = 0;
        int status = CoreAudioInterop.AudioObjectGetPropertyDataSize(
            CoreAudioInterop.kAudioObjectSystemObject,
            ref address,
            0,
            IntPtr.Zero,
            out dataSize);

        if (status != CoreAudioInterop.noErr || dataSize == 0)
        {
            return Array.Empty<uint>();
        }

        // デバイスIDの取得
        int deviceCount = (int)(dataSize / sizeof(uint));
        uint[] deviceIds = new uint[deviceCount];
        IntPtr dataPtr = Marshal.AllocHGlobal((int)dataSize);
        try
        {
            status = CoreAudioInterop.AudioObjectGetPropertyData(
                CoreAudioInterop.kAudioObjectSystemObject,
                ref address,
                0,
                IntPtr.Zero,
                ref dataSize,
                dataPtr);

            if (status == CoreAudioInterop.noErr)
            {
                Marshal.Copy(dataPtr, (int[])(object)deviceIds, 0, deviceCount);
            }
            else
            {
                return Array.Empty<uint>();
            }
        }
        finally
        {
            Marshal.FreeHGlobal(dataPtr);
        }

        return deviceIds;
    }

    /// <summary>
    /// デバイスが指定されたスコープのストリームを持つか確認
    /// </summary>
    private bool DeviceHasStreams(uint deviceId, uint scope)
    {
        var address = new CoreAudioInterop.AudioObjectPropertyAddress
        {
            Selector = CoreAudioInterop.kAudioDevicePropertyStreams,
            Scope = scope,
            Element = CoreAudioInterop.kAudioObjectPropertyElementMain
        };

        // プロパティの存在確認
        int hasProperty = CoreAudioInterop.AudioObjectHasProperty(deviceId, ref address);
        if (hasProperty == 0)
        {
            return false;
        }

        // ストリーム数の確認
        uint dataSize = 0;
        int status = CoreAudioInterop.AudioObjectGetPropertyDataSize(
            deviceId,
            ref address,
            0,
            IntPtr.Zero,
            out dataSize);

        return status == CoreAudioInterop.noErr && dataSize > 0;
    }

    /// <summary>
    /// デバイス名の取得
    /// </summary>
    private string GetDeviceName(uint deviceId)
    {
        var address = new CoreAudioInterop.AudioObjectPropertyAddress
        {
            Selector = CoreAudioInterop.kAudioObjectPropertyName,
            Scope = CoreAudioInterop.kAudioObjectPropertyScopeGlobal,
            Element = CoreAudioInterop.kAudioObjectPropertyElementMain
        };

        // プロパティサイズの取得
        uint dataSize = 0;
        int status = CoreAudioInterop.AudioObjectGetPropertyDataSize(
            deviceId,
            ref address,
            0,
            IntPtr.Zero,
            out dataSize);

        if (status != CoreAudioInterop.noErr || dataSize == 0)
        {
            return $"Unknown Device (ID: {deviceId})";
        }

        // デバイス名の取得（CFStringRef）
        IntPtr dataPtr = Marshal.AllocHGlobal((int)dataSize);
        try
        {
            status = CoreAudioInterop.AudioObjectGetPropertyData(
                deviceId,
                ref address,
                0,
                IntPtr.Zero,
                ref dataSize,
                dataPtr);

            if (status == CoreAudioInterop.noErr)
            {
                // CFStringRefからC#文字列への変換
                IntPtr cfStringPtr = Marshal.ReadIntPtr(dataPtr);
                if (cfStringPtr != IntPtr.Zero)
                {
                    string? deviceName = CFStringToString(cfStringPtr);
                    if (!string.IsNullOrEmpty(deviceName))
                    {
                        return deviceName;
                    }
                }
            }

            return $"Unknown Device (ID: {deviceId})";
        }
        finally
        {
            Marshal.FreeHGlobal(dataPtr);
        }
    }

    /// <summary>
    /// CFStringRefからC#文字列への変換
    /// </summary>
    private string? CFStringToString(IntPtr cfStringRef)
    {
        // CFStringGetLength
        int length = CFStringGetLength(cfStringRef);
        if (length == 0)
            return null;

        // CFStringGetCString
        int maxBufferSize = length * 4 + 1; // UTF-8の最大サイズ
        IntPtr buffer = Marshal.AllocHGlobal(maxBufferSize);
        try
        {
            const uint kCFStringEncodingUTF8 = 0x08000100;
            bool success = CFStringGetCString(cfStringRef, buffer, maxBufferSize, kCFStringEncodingUTF8);
            if (success)
            {
                return Marshal.PtrToStringUTF8(buffer);
            }
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    #region CoreFoundation P/Invoke

    private const string CoreFoundationFramework = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport(CoreFoundationFramework)]
    private static extern int CFStringGetLength(IntPtr theString);

    [DllImport(CoreFoundationFramework)]
    private static extern bool CFStringGetCString(IntPtr theString, IntPtr buffer, int bufferSize, uint encoding);

    #endregion
}
