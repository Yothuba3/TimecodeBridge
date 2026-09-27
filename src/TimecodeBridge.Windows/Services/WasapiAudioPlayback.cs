using NAudio.CoreAudioApi;
using NAudio.Wave;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Windows.Services;

/// <summary>
/// WASAPI(共有モード)の出力。<see cref="WriteSamples"/> で受けた 16bit モノラル PCM をバッファし、
/// 空のときは無音を流す。サンプルレートはデバイスのミックスフォーマットに合わせる(LTC を再サンプルさせない)。
/// </summary>
public sealed class WasapiAudioPlayback : IAudioPlayback
{
    private const int LatencyMs = 100;
    private const int BufferSeconds = 5;

    private readonly object _lock = new();
    private WasapiOut? _output;
    private BufferedWaveProvider? _buffer;
    private bool _disposed;

    public int SampleRate { get; private set; }

    public void Start(AudioDeviceInfo device)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(device);

        lock (_lock)
        {
            Stop();
            using var enumerator = new MMDeviceEnumerator();
            var mmDevice = enumerator.GetDevice(device.Id);
            SampleRate = mmDevice.AudioClient.MixFormat.SampleRate;

            var buffer = new BufferedWaveProvider(new NAudio.Wave.WaveFormat(SampleRate, 16, 1))
            {
                BufferDuration = TimeSpan.FromSeconds(BufferSeconds),
                DiscardOnBufferOverflow = true,
                ReadFully = true,
            };
            var output = new WasapiOut(mmDevice, AudioClientShareMode.Shared, true, LatencyMs);
            try
            {
                output.Init(buffer);
                output.Play();
            }
            catch
            {
                output.Dispose();
                throw;
            }
            _buffer = buffer;
            _output = output;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_output is null) return;
            try { _output.Stop(); } catch { /* 停止時の例外は無視して破棄する */ }
            _output.Dispose();
            _output = null;
            _buffer = null;
        }
    }

    public void WriteSamples(byte[] samples, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)offset, (uint)samples.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)count, (uint)(samples.Length - offset));

        lock (_lock)
        {
            _buffer?.AddSamples(samples, offset, count);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
