using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Core.Services.Interfaces;

/// <summary>
/// オーディオデバイスの列挙機能を提供するサービス
/// </summary>
public interface IAudioDeviceService
{
    /// <summary>
    /// アクティブなキャプチャ（入力）デバイスの一覧を取得する
    /// </summary>
    IReadOnlyList<AudioDeviceInfo> GetCaptureDevices();

    /// <summary>
    /// アクティブなレンダー（出力/ループバック）デバイスの一覧を取得する
    /// </summary>
    IReadOnlyList<AudioDeviceInfo> GetRenderDevices();
}
