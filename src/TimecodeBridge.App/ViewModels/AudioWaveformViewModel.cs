using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.App.ViewModels;

/// <summary>
/// 波形表示用に、直近の生サンプルをリングバッファへ貯める。
/// 間引きはせず、描画側が末尾から必要な区間（既定0.5フレーム相当）を取り出して
/// min/max縮約で描く。間引くとLTC矩形のピークを取りこぼし、密な塊にしか見えないため。
/// </summary>
public class AudioWaveformViewModel
{
    // LTC入力は48kHz固定。約2フレーム分の生サンプルを保持する（30fpsで1600、余裕を見て4096）。
    public const int SampleRate = 48000;
    private const int RingSize = 4096;

    private readonly float[] _ring = new float[RingSize];
    private readonly object _gate = new();
    private int _writePos;
    private long _totalWritten;
    private float _peakLevel;
    private volatile bool _dirty;

    public AudioWaveformViewModel(ITimecodeEngine timecodeEngine)
    {
        timecodeEngine.AudioSamplesAvailable += OnAudioSamplesAvailable;
    }

    /// <summary>前回チェック以降に新データがあれば true とピークレベルを返す。</summary>
    public bool ConsumeUpdate(out float peakLevel)
    {
        lock (_gate) peakLevel = _peakLevel;
        if (!_dirty) return false;
        _dirty = false;
        return true;
    }

    /// <summary>
    /// 末尾から <paramref name="sampleCount"/> 個の生サンプルを時系列順に取り出す。
    /// まだ十分に貯まっていない場合は、先頭側を無音(0)で埋める。
    /// </summary>
    public void CopyRecent(float[] destination, int sampleCount)
    {
        if (sampleCount > RingSize) sampleCount = RingSize;
        lock (_gate)
        {
            long available = _totalWritten;
            int start = destination.Length - sampleCount; // destination先頭を無音で埋める余地
            for (int i = 0; i < start; i++) destination[i] = 0f;

            for (int i = 0; i < sampleCount; i++)
            {
                long globalIndex = available - sampleCount + i;
                if (globalIndex < 0)
                {
                    destination[start + i] = 0f;
                    continue;
                }
                destination[start + i] = _ring[(int)(globalIndex % RingSize)];
            }
        }
    }

    private void OnAudioSamplesAvailable(object? sender, AudioSamplesEventArgs e)
    {
        var samples = e.Samples;
        float peak = 0f;

        lock (_gate)
        {
            foreach (var s in samples)
            {
                _ring[_writePos] = s;
                _writePos = (_writePos + 1) % RingSize;
                _totalWritten++;

                float abs = s < 0 ? -s : s;
                if (abs > peak) peak = abs;
            }
            _peakLevel = peak;
        }
        _dirty = true;
    }
}
