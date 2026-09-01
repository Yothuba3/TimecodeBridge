using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Services;

/// <summary>
/// LTCキャプチャデバイスの初期化・管理・停止を担当する内部クラス。
/// WasapiCapture/WasapiLoopbackCaptureのライフサイクル管理、LtcDecoderへのサンプルデータ中継、
/// デコード結果およびオーディオ波形のコールバック通知を行う。
/// </summary>
internal class LtcCaptureController : IDisposable
{
    private WasapiCapture? _wasapiCapture;
    private LtcDecoder? _ltcDecoder;
    private readonly ManualResetEventSlim _captureStoppedEvent = new(true);

    internal Action<TimecodeValue>? OnFrameDecoded { get; set; }
    internal Action<float[]>? OnAudioSamplesAvailable { get; set; }

    internal void Start(string audioDeviceId, bool isLoopback, int frameRate)
    {
        Stop();

        _ltcDecoder = new LtcDecoder();

        var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(audioDeviceId);
        _wasapiCapture = isLoopback
            ? new WasapiLoopbackCapture(device)
            : new WasapiCapture(device);

        var waveFormat = _wasapiCapture.WaveFormat;
        _ltcDecoder.Initialize(waveFormat.SampleRate, frameRate);

        int channels = waveFormat.Channels;
        int bitsPerSample = waveFormat.BitsPerSample;
        int sampleRate = waveFormat.SampleRate;

        _ltcDecoder.FrameDecoded += (_, timecodeValue) => OnFrameDecoded?.Invoke(timecodeValue);

        _wasapiCapture.DataAvailable += (_, e) =>
        {
            try
            {
                _ltcDecoder?.ProcessSamples(e.Buffer, e.BytesRecorded, sampleRate, bitsPerSample, channels);

                if (OnAudioSamplesAvailable != null)
                {
                    var samples = ExtractMonoSamples(e.Buffer, e.BytesRecorded, bitsPerSample, channels);
                    OnAudioSamplesAvailable.Invoke(samples);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning($"LTC capture processing error: {ex.Message}");
            }
        };

        _wasapiCapture.RecordingStopped += (_, _) =>
        {
            _captureStoppedEvent.Set();
        };

        _captureStoppedEvent.Reset();
        _wasapiCapture.StartRecording();
    }

    internal void Stop()
    {
        if (_wasapiCapture != null)
        {
            try
            {
                _wasapiCapture.StopRecording();
            }
            catch (InvalidOperationException ex)
            {
                System.Diagnostics.Trace.TraceWarning($"LTC capture already stopped: {ex.Message}");
            }

            _captureStoppedEvent.Wait(2000);
            _wasapiCapture.Dispose();
            _wasapiCapture = null;
        }

        if (_ltcDecoder != null)
        {
            _ltcDecoder.Dispose();
            _ltcDecoder = null;
        }
    }

    public void Dispose()
    {
        Stop();
        _captureStoppedEvent.Dispose();
    }

    internal static float[] ExtractMonoSamples(byte[] buffer, int bytesRecorded, int bitsPerSample, int channels)
    {
        int bytesPerSample = bitsPerSample / 8;
        int bytesPerFrame = bytesPerSample * channels;
        int frameCount = bytesRecorded / bytesPerFrame;
        var samples = new float[frameCount];

        for (int i = 0; i < frameCount; i++)
        {
            int offset = i * bytesPerFrame;
            samples[i] = bitsPerSample switch
            {
                32 => BitConverter.ToSingle(buffer, offset),
                16 => BitConverter.ToInt16(buffer, offset) / 32768f,
                _ => 0f,
            };
        }

        return samples;
    }
}
