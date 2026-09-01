using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;

namespace TimecodeBridge.Core.Services.Interfaces;

/// <summary>
/// オーディオキャプチャインターフェース
/// プラットフォーム固有の実装（Windows: NAudio WASAPI, macOS: CoreAudio）をサポート
/// </summary>
public interface IAudioCapture : IDisposable
{
    /// <summary>
    /// オーディオキャプチャを開始
    /// </summary>
    /// <param name="device">キャプチャデバイス情報</param>
    void Start(AudioDeviceInfo device);

    /// <summary>
    /// オーディオキャプチャを停止
    /// </summary>
    void Stop();

    /// <summary>
    /// オーディオサンプルが利用可能になったときに発火
    /// </summary>
    event EventHandler<AudioSamplesEventArgs>? AudioSamplesAvailable;

    /// <summary>
    /// エラーが発生したときに発火
    /// </summary>
    event EventHandler<AudioErrorEventArgs>? ErrorOccurred;
}
