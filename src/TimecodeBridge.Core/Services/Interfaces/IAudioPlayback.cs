using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Core.Services.Interfaces;

/// <summary>
/// オーディオプレイバックインターフェース
/// プラットフォーム固有の実装（Windows: NAudio WASAPI, macOS: CoreAudio）をサポート
/// </summary>
public interface IAudioPlayback : IDisposable
{
    /// <summary>
    /// オーディオプレイバックを開始
    /// </summary>
    /// <param name="device">プレイバックデバイス情報</param>
    void Start(AudioDeviceInfo device);

    /// <summary>
    /// オーディオプレイバックを停止
    /// </summary>
    void Stop();

    /// <summary>
    /// オーディオサンプルをデバイスに書き込み
    /// </summary>
    /// <param name="samples">オーディオサンプルバッファ</param>
    /// <param name="offset">バッファ内のオフセット</param>
    /// <param name="count">書き込むバイト数</param>
    void WriteSamples(byte[] samples, int offset, int count);
}
