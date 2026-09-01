using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.App.Services;

/// <summary>
/// macOS版オーディオデバイスサービス（stub実装）
/// Phase 3でCoreAudio実装予定
/// </summary>
public class AudioDeviceService : IAudioDeviceService
{
    /// <summary>
    /// キャプチャ（入力）デバイスの一覧を取得（stub実装）
    /// </summary>
    /// <returns>ダミーのキャプチャデバイスリスト</returns>
    public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices()
    {
        // TODO: Phase 3でCoreAudio APIを使用した実装
        // 現在はダミーデータを返却
        return new List<AudioDeviceInfo>
        {
            new AudioDeviceInfo("stub-capture-1", "Built-in Microphone (Stub)", false),
            new AudioDeviceInfo("stub-capture-2", "External Microphone (Stub)", false)
        };
    }

    /// <summary>
    /// レンダー（出力/ループバック）デバイスの一覧を取得（stub実装）
    /// </summary>
    /// <returns>ダミーのレンダーデバイスリスト</returns>
    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
    {
        // TODO: Phase 3でCoreAudio APIを使用した実装
        // 現在はダミーデータを返却
        return new List<AudioDeviceInfo>
        {
            new AudioDeviceInfo("stub-render-1", "Built-in Output (Stub)", false),
            new AudioDeviceInfo("stub-render-2", "Built-in Output Loopback (Stub)", true)
        };
    }
}
