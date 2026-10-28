using System.Diagnostics;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Core.Services;

public class TimecodeGenerator : ITimecodeGenerator, IDisposable
{
    private volatile bool _isRunning;
    private TimecodeValue _currentTimecode;
    private TimecodeValue _startTime;
    private FrameRate _frameRate;
    private long _totalFramesGenerated;

    // ループ 1 周のフレーム数。0 ならループしない。
    // 経過フレーム数をこれで割った余りを開始位置に足すので、
    // 何周しても境界がずれない（周回ごとの端数が積み上がらない）。
    private long _loopFrames;
    private CancellationTokenSource? _timerCts;
    private Stopwatch? _stopwatch;
    private readonly object _lock = new();

    public TimecodeValue CurrentTimecode
    {
        get { lock (_lock) { return _currentTimecode; } }
    }

    public bool IsRunning => _isRunning;

    public event EventHandler<TimecodeValue>? FrameGenerated;

    public void Start(TimecodeValue startTime, FrameRate frameRate)
        => Start(startTime, frameRate, null);

    /// <summary>
    /// <paramref name="loopEndTime"/> を与えると、開始時刻からそこまでを繰り返す。
    /// 終端フレームは鳴らしてから開始時刻へ戻るので、指定した範囲が丸ごと出る。
    /// 開始時刻以下の終端は範囲にならないため、ループなしとして扱う。
    /// </summary>
    public void Start(TimecodeValue startTime, FrameRate frameRate, TimecodeValue? loopEndTime)
    {
        if (_isRunning)
            return;

        _startTime = startTime;
        _frameRate = frameRate;
        _loopFrames = LoopFramesFor(startTime, frameRate, loopEndTime);
        _totalFramesGenerated = 0;
        lock (_lock) { _currentTimecode = startTime; }

        StartTimerLoop();
    }

    private static long LoopFramesFor(TimecodeValue startTime, FrameRate frameRate, TimecodeValue? loopEndTime)
    {
        if (loopEndTime is not { } end) return 0;
        long length = new TimecodeValue(end.Hours, end.Minutes, end.Seconds, end.Frames, frameRate).TotalFrames()
                    - new TimecodeValue(startTime.Hours, startTime.Minutes, startTime.Seconds, startTime.Frames, frameRate).TotalFrames();
        return length > 0 ? length + 1 : 0;
    }

    public void Resume()
    {
        if (_isRunning)
            return;

        // Resume from current position (_totalFramesGenerated is preserved)
        StartTimerLoop();
    }

    public void Stop()
    {
        lock (_lock)
        {
            _timerCts?.Cancel();
            _isRunning = false;
        }
    }

    public void Reset()
    {
        ResetTo(_startTime);
    }

    public void ResetTo(TimecodeValue startTime)
    {
        bool wasRunning = _isRunning;
        if (wasRunning)
        {
            _timerCts?.Cancel();
        }

        // 別の位置へ飛ばすならループ範囲は無効になる。
        // 元の範囲のまま回すと、頼まれていない区間を繰り返すことになる。
        if (startTime.TotalFrames() != _startTime.TotalFrames()) _loopFrames = 0;

        _startTime = startTime;
        _totalFramesGenerated = 0;
        lock (_lock) { _currentTimecode = _startTime; }

        FrameGenerated?.Invoke(this, _startTime);

        if (wasRunning)
        {
            StartTimerLoop();
        }
    }

    public void Dispose()
    {
        Stop();
        _timerCts?.Dispose();
    }

    private void StartTimerLoop()
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        var fps = _frameRate.FramesPerSecond();

        // 動いている印はここで立てる。Task の中で立てると、開始の直後に来た Stop がまだ動いていないと見て素通りし、
        // そのあと動き出したループが止まらなくなる(止めたまま FPS を変えてリセットすると「停止」のまま LTC が出続けた)
        lock (_lock)
        {
            _timerCts = cts;
            _isRunning = true;
        }

        _ = Task.Run(async () =>
        {
            _stopwatch = Stopwatch.StartNew();
            var baseFrames = _totalFramesGenerated;
            var timer = new PeriodicTimer(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / fps));
            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    double elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
                    long expectedFrames = baseFrames + (long)(elapsedSeconds * fps);

                    // 止められたら、遅れを取り戻している途中でも残りのフレームは出さない
                    while (!token.IsCancellationRequested && _totalFramesGenerated < expectedFrames)
                    {
                        _totalFramesGenerated++;
                        long advanced = _loopFrames > 0 ? _totalFramesGenerated % _loopFrames : _totalFramesGenerated;
                        long totalFrames = _startTime.TotalFrames() + advanced;
                        var tc = TimecodeValue.FromTotalFrames(totalFrames, _frameRate);
                        lock (_lock) { _currentTimecode = tc; }
                        FrameGenerated?.Invoke(this, tc);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when Stop() is called
            }
            finally
            {
                timer.Dispose();
                // 止めてすぐ動かし直した(リセット・再開)なら、今動いているのは次のループなので印を下ろさない
                lock (_lock)
                {
                    if (_timerCts == cts) _isRunning = false;
                }
            }
        }, token);
    }
}
