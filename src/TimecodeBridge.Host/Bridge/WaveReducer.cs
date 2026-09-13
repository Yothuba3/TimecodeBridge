namespace TimecodeBridge.Host.Bridge;

/// <summary>
/// 直近の入力サンプルを保持し、表示幅の点数へ min/max で縮約する。書き込みは音声スレッド、読み出しは UI スレッド。
/// </summary>
public sealed class WaveReducer
{
    /// <summary>表示区間。UI 改修前(v2 の AudioWaveformView)と同じ約 0.1 フレーム分(30fps 基準、約 3.3ms)。LTC の矩形が数個見える幅。</summary>
    public const double WindowSeconds = 0.1 / 30.0;

    private readonly object _lock = new();
    private float[] _ring = new float[SamplesFor(48000)];
    private int _write;
    private int _count;
    private float _peak;

    public int WindowSamples => _ring.Length;

    private static int SamplesFor(int sampleRate) => Math.Max(16, (int)(sampleRate * WindowSeconds));

    /// <summary>実際のサンプルレートに合わせて時間窓を保つ(同じなら何もしない)。</summary>
    public void Configure(int sampleRate)
    {
        int n = SamplesFor(sampleRate);
        lock (_lock)
        {
            if (_ring.Length == n) return;
            _ring = new float[n];
            _write = 0;
            _count = 0;
        }
    }

    public void Write(ReadOnlySpan<float> samples)
    {
        lock (_lock)
        {
            float peak = _peak;
            foreach (var s in samples)
            {
                if (!float.IsFinite(s)) continue;
                _ring[_write] = s;
                _write = (_write + 1) % _ring.Length;
                if (_count < _ring.Length) _count++;
                float a = Math.Abs(s);
                if (a > peak) peak = a;
            }
            _peak = peak;
        }
    }

    /// <summary>points 点に縮約した [min,max] を返し、ピークを消費(リセット)する。データが無ければ null。</summary>
    public (float[] Min, float[] Max, double? LevelDbfs)? Snapshot(int points)
    {
        points = Math.Clamp(points, 16, 2048);
        lock (_lock)
        {
            if (_count == 0) return null;
            var min = new float[points];
            var max = new float[points];
            int start = (_write - _count + _ring.Length) % _ring.Length;
            for (int p = 0; p < points; p++)
            {
                int from = (int)((long)p * _count / points);
                int to = Math.Max(from + 1, (int)((long)(p + 1) * _count / points));
                float lo = 1f, hi = -1f;
                for (int i = from; i < to; i++)
                {
                    float v = _ring[(start + i) % _ring.Length];
                    if (v < lo) lo = v;
                    if (v > hi) hi = v;
                }
                min[p] = lo;
                max[p] = hi;
            }
            double? level = _peak > 0 ? 20 * Math.Log10(_peak) : null;
            _peak = 0;
            return (min, max, level);
        }
    }
}
