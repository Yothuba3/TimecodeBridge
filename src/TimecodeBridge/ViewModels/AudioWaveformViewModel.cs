using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.ViewModels;

public class AudioWaveformViewModel
{
    public const int DisplaySampleCount = 512;

    private readonly float[] _displayBuffer = new float[DisplaySampleCount];
    private volatile int _writePos;
    private volatile float _peakLevel;
    private volatile bool _dirty;

    public AudioWaveformViewModel(ITimecodeEngine timecodeEngine)
    {
        timecodeEngine.AudioSamplesAvailable += OnAudioSamplesAvailable;
    }

    /// <summary>
    /// Returns true and resets the dirty flag if new data is available since last check.
    /// </summary>
    public bool ConsumeUpdate(out float peakLevel)
    {
        peakLevel = _peakLevel;
        if (!_dirty) return false;
        _dirty = false;
        return true;
    }

    /// <summary>
    /// Copies the current circular buffer into a destination array in display order.
    /// </summary>
    public void CopyDisplayBuffer(float[] destination)
    {
        int pos = _writePos;
        for (int i = 0; i < DisplaySampleCount; i++)
        {
            destination[i] = _displayBuffer[(pos + i) % DisplaySampleCount];
        }
    }

    private void OnAudioSamplesAvailable(object? sender, AudioSamplesEventArgs e)
    {
        var samples = e.Samples;

        // Downsample: pick every Nth sample to fill the display buffer
        int step = Math.Max(1, samples.Length / 64);
        int pos = _writePos;
        for (int i = 0; i < samples.Length; i += step)
        {
            _displayBuffer[pos] = samples[i];
            pos = (pos + 1) % DisplaySampleCount;
        }
        _writePos = pos;

        // Calculate peak level
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float abs = Math.Abs(samples[i]);
            if (abs > peak) peak = abs;
        }
        _peakLevel = peak;
        _dirty = true;
    }
}
