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
    {
        if (_isRunning)
            return;

        _startTime = startTime;
        _frameRate = frameRate;
        _totalFramesGenerated = 0;
        lock (_lock) { _currentTimecode = startTime; }

        StartTimerLoop();
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
        if (!_isRunning)
            return;

        _timerCts?.Cancel();
        _isRunning = false;
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
        _timerCts = new CancellationTokenSource();
        var token = _timerCts.Token;
        var fps = _frameRate.FramesPerSecond();

        _ = Task.Run(async () =>
        {
            _isRunning = true;
            _stopwatch = Stopwatch.StartNew();
            var baseFrames = _totalFramesGenerated;
            var timer = new PeriodicTimer(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / fps));
            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    double elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
                    long expectedFrames = baseFrames + (long)(elapsedSeconds * fps);

                    while (_totalFramesGenerated < expectedFrames)
                    {
                        _totalFramesGenerated++;
                        long totalFrames = _startTime.TotalFrames() + _totalFramesGenerated;
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
                _isRunning = false;
            }
        }, token);
    }
}
