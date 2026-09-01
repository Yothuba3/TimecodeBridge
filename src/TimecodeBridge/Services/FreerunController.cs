using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Services;

/// <summary>
/// 信号喪失時のタイムコードフレーム自動補完を担当する内部クラス。
/// 最終受信フレームからの連続フレーム生成、Stopwatchベースの精密タイミング制御、
/// 指定時間経過後の自動停止を行う。
/// </summary>
internal class FreerunController : IDisposable
{
    private volatile bool _isFreerunning;
    private CancellationTokenSource? _freerunCts;
    private Timer? _freerunExpiryTimer;

    internal Action<TimecodeValue, TimecodeValue>? OnFrameGenerated { get; set; }
    internal Action? OnExpired { get; set; }
    internal bool IsFreerunning => _isFreerunning;

    internal void Start(TimecodeValue lastRawFrame, TimecodeOffset offset, FrameRate frameRate, double durationSeconds)
    {
        Stop();

        _isFreerunning = true;
        _freerunCts = new CancellationTokenSource();
        var token = _freerunCts.Token;

        var expiryMs = (int)(durationSeconds * 1000);
        _freerunExpiryTimer = new Timer(_ =>
        {
            // OnExpired must fire before Stop so the caller can check IsFreerunning
            OnExpired?.Invoke();
        }, null, expiryMs, Timeout.Infinite);

        var fps = frameRate.FramesPerSecond();
        var intervalMs = 1000.0 / fps;
        var lastTotalFrames = lastRawFrame.TotalFrames();

        Thread freerunThread = new(() =>
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            long frameCount = 0;

            while (!token.IsCancellationRequested)
            {
                frameCount++;
                var nextFrameTime = frameCount * intervalMs;

                while (stopwatch.Elapsed.TotalMilliseconds < nextFrameTime)
                {
                    if (token.IsCancellationRequested) return;
                    Thread.Sleep(1);
                }

                if (token.IsCancellationRequested) return;

                var newTotalFrames = lastTotalFrames + frameCount;
                var rawFrame = TimecodeValue.FromTotalFrames(newTotalFrames, frameRate);
                var offsetFrame = rawFrame.Add(offset);

                OnFrameGenerated?.Invoke(rawFrame, offsetFrame);
            }
        })
        {
            Name = "FreerunController-Worker",
            IsBackground = true,
        };
        freerunThread.Start();
    }

    internal void Stop()
    {
        if (!_isFreerunning) return;

        _freerunCts?.Cancel();
        _freerunCts?.Dispose();
        _freerunCts = null;

        _freerunExpiryTimer?.Dispose();
        _freerunExpiryTimer = null;

        _isFreerunning = false;
    }

    public void Dispose()
    {
        Stop();
    }
}
