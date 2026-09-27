using NAudio.CoreAudioApi;
using NAudio.Wave;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Windows.Services;

/// <summary>
/// WASAPI(共有モード)の入力。ループバックデバイスなら再生音を取り込む。
/// サンプルは先頭チャンネルを float モノラルにして <see cref="AudioSamplesAvailable"/> で渡す(呼び出しは NAudio のキャプチャスレッド)。
/// </summary>
public sealed class WasapiAudioCapture : IAudioCapture
{
    private readonly object _lock = new();
    private WasapiCapture? _capture;
    private bool _disposed;

    public int SampleRate { get; private set; }

    public event EventHandler<AudioSamplesEventArgs>? AudioSamplesAvailable;
    public event EventHandler<AudioErrorEventArgs>? ErrorOccurred;

    public void Start(AudioDeviceInfo device)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(device);

        lock (_lock)
        {
            Stop();
            using var enumerator = new MMDeviceEnumerator();
            var mmDevice = enumerator.GetDevice(device.Id);
            var capture = device.IsLoopback ? new WasapiLoopbackCapture(mmDevice) : new WasapiCapture(mmDevice);
            var format = capture.WaveFormat;
            SampleRate = format.SampleRate;

            capture.DataAvailable += (_, e) =>
            {
                try
                {
                    var samples = WasapiSampleConverter.ToMonoFloat(e.Buffer, e.BytesRecorded, format);
                    if (samples.Length > 0) AudioSamplesAvailable?.Invoke(this, new AudioSamplesEventArgs(samples));
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, new AudioErrorEventArgs($"入力データの変換に失敗: {ex.Message}", ex));
                }
            };
            capture.RecordingStopped += (_, e) =>
            {
                if (e.Exception is not null)
                    ErrorOccurred?.Invoke(this, new AudioErrorEventArgs($"入力が停止しました: {e.Exception.Message}", e.Exception));
            };

            try
            {
                capture.StartRecording();
            }
            catch
            {
                capture.Dispose();
                throw;
            }
            _capture = capture;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_capture is null) return;
            try { _capture.StopRecording(); } catch { /* 停止時の例外は無視して破棄する */ }
            _capture.Dispose();
            _capture = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
