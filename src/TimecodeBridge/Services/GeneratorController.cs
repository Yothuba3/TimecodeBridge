using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Services;

/// <summary>
/// ジェネレーターの開始・一時停止・再開・リセット・破棄を担当する内部クラス。
/// TimecodeGenerator/LtcEncoder/WasapiOutのライフサイクル管理を行う。
/// 音声出力デバイスが利用不可でもフレーム生成は継続する（グレースフルデグラデーション）。
/// </summary>
internal class GeneratorController : IDisposable
{
    private TimecodeGenerator? _generator;
    private LtcEncoder? _ltcEncoder;
    private WasapiOut? _wasapiOut;

    internal Action<TimecodeValue>? OnFrameGenerated { get; set; }

    internal void Start(GeneratorSettings settings)
    {
        Cleanup();

        _generator = new TimecodeGenerator();
        _ltcEncoder = new LtcEncoder();

        const int sampleRate = 48000;
        _ltcEncoder.Initialize(sampleRate, settings.FrameRate);
        _ltcEncoder.VolumeLevel = settings.VolumeLevel;

        _generator.FrameGenerated += (_, tc) =>
        {
            OnFrameGenerated?.Invoke(tc);
            _ltcEncoder?.EnqueueFrame(tc);
        };

        if (!string.IsNullOrEmpty(settings.OutputDeviceId))
        {
            try
            {
                var enumerator = new MMDeviceEnumerator();
                var device = enumerator.GetDevice(settings.OutputDeviceId);
                _wasapiOut = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
                _wasapiOut.Init(new LtcEncoderWaveProvider(_ltcEncoder));
                _wasapiOut.Play();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning($"LTC output device error: {ex.Message}");
                _wasapiOut?.Dispose();
                _wasapiOut = null;
            }
        }

        _generator.Start(settings.StartTime, settings.FrameRate);
    }

    internal void Resume()
    {
        if (_generator == null) return;

        if (_ltcEncoder != null && _wasapiOut != null)
        {
            try { _wasapiOut.Play(); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning($"LTC output resume error: {ex.Message}");
            }
        }

        _generator.Resume();
    }

    internal void Pause()
    {
        _generator?.Stop();

        if (_wasapiOut != null)
        {
            try { _wasapiOut.Pause(); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning($"LTC output pause error: {ex.Message}");
            }
        }
    }

    internal void Reset()
    {
        _generator?.Reset();
    }

    public void Dispose()
    {
        Cleanup();
    }

    private void Cleanup()
    {
        if (_generator != null)
        {
            _generator.Stop();
            _generator.Dispose();
            _generator = null;
        }

        if (_wasapiOut != null)
        {
            try { _wasapiOut.Stop(); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning($"LTC output stop error: {ex.Message}");
            }
            _wasapiOut.Dispose();
            _wasapiOut = null;
        }

        if (_ltcEncoder != null)
        {
            _ltcEncoder.Reset();
            _ltcEncoder = null;
        }
    }
}
