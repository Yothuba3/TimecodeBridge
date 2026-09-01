using System.Threading.Channels;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.App.Services;

/// <summary>
/// macOS版タイムコードエンジン。
/// LTCキャプチャは IAudioCapture(CoreAudio)、LTC出力は IAudioPlayback(CoreAudio)を使用する。
/// </summary>
public class TimecodeEngine : ITimecodeEngine, IDisposable
{
    private const int SampleRate = 48000;
    private const int SignalLossTimeoutMs = 500;

    // CoreAudioPlayback は内部バッファ(最大5秒)から再生するため、
    // 実時間より少し先行して書き込み続けることでアンダーランを防ぐ
    private const int PlaybackLeadMs = 100;
    private const int PlaybackFeedIntervalMs = 10;

    private readonly IAudioDeviceService _audioDeviceService;
    private readonly Func<IAudioCapture> _captureFactory;
    private readonly Func<IAudioPlayback> _playbackFactory;

    private readonly Channel<(int Session, TimecodeValue Frame)> _channel;

    // Start/Stopのたびに増える世代番号。停止前に積まれた古いフレームが
    // 停止後に処理されて受信状態へ戻るのを防ぐ（フレームに世代を付けて破棄する）
    private int _session;
    private readonly CancellationTokenSource _cts;
    private readonly Thread _workerThread;
    private readonly object _lock = new();
    private readonly Timer _signalLossTimer;

    // LTC capture
    private IAudioCapture? _capture;
    private LtcDecoder? _ltcDecoder;

    // Generator
    private TimecodeGenerator? _generator;
    private LtcEncoder? _ltcEncoder;
    private IAudioPlayback? _playback;
    private CancellationTokenSource? _playbackFeedCts;
    private Thread? _playbackFeedThread;

    // Freerun state
    private double _freerunDurationSeconds;
    private volatile bool _isFreerunning;
    private CancellationTokenSource? _freerunCts;
    private Timer? _freerunExpiryTimer;

    // LTC decode continuity gate:
    // 長時間の無信号中はノイズが偶然フレームとして解析されることがあり、
    // それがフレームレート自動判定を汚染すると本物のTC復帰後も誤解釈が続く。
    // 前フレームと連続する2フレームが揃うまで採用しないことで孤立ノイズを排除する。
    private long _ltcLastAcceptedOrdinal = -1;
    private long _ltcPendingOrdinal = -1;
    private TimecodeValue? _ltcPendingFrame;

    // LTC frame rate auto-detection
    private bool _ltcAutoDetectActive;
    private int _ltcMaxFrameSeen;
    private bool _ltcDropFrameSeen;

    private volatile bool _isReceiving;
    private volatile bool _disposed;
    private TimecodeValue _currentRawTimecode;
    private TimecodeValue _currentOffsetTimecode;
    private TimecodeOffset _offset;
    private TimecodeSourceType _activeSource;

    public TimecodeValue CurrentRawTimecode
    {
        get { lock (_lock) return _currentRawTimecode; }
    }

    public TimecodeValue CurrentOffsetTimecode
    {
        get { lock (_lock) return _currentOffsetTimecode; }
    }

    public TimecodeOffset Offset
    {
        get { lock (_lock) return _offset; }
        set { lock (_lock) _offset = value; }
    }

    public FrameRate FrameRate { get; set; }

    public TimecodeSourceType ActiveSource
    {
        get { lock (_lock) return _activeSource; }
        private set { lock (_lock) _activeSource = value; }
    }

    public bool IsReceiving => _isReceiving;

    public double FreerunDurationSeconds
    {
        get { lock (_lock) return _freerunDurationSeconds; }
        set { lock (_lock) _freerunDurationSeconds = value; }
    }

    public bool IsFreerunning => _isFreerunning;

    public event EventHandler<TimecodeUpdatedEventArgs>? TimecodeUpdated;
    public event EventHandler<TimecodeStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<AudioSamplesEventArgs>? AudioSamplesAvailable;

    /// <summary>
    /// オーディオ入出力で発生したエラー(TCC権限拒否など)
    /// </summary>
    public event EventHandler<AudioErrorEventArgs>? AudioErrorOccurred;

    public TimecodeEngine(
        FrameRate frameRate,
        IAudioDeviceService audioDeviceService,
        Func<IAudioCapture> captureFactory,
        Func<IAudioPlayback> playbackFactory)
    {
        _audioDeviceService = audioDeviceService;
        _captureFactory = captureFactory;
        _playbackFactory = playbackFactory;

        FrameRate = frameRate;
        _offset = TimecodeOffset.Zero(frameRate);

        _channel = Channel.CreateUnbounded<(int Session, TimecodeValue Frame)>(
            new UnboundedChannelOptions { SingleWriter = true });

        _cts = new CancellationTokenSource();
        _signalLossTimer = new Timer(OnSignalLossTimeout, null, Timeout.Infinite, Timeout.Infinite);

        _workerThread = new Thread(WorkerLoop)
        {
            Name = "TimecodeEngine-Worker",
            IsBackground = true,
        };
        _workerThread.Start();
    }

    internal void WriteFrame(TimecodeValue frame)
    {
        if (_disposed) return;
        _channel.Writer.TryWrite((Volatile.Read(ref _session), frame));
    }

    /// <summary>
    /// デコード済みLTCフレームを連続性ゲートを通してパイプラインへ渡す。
    /// 直前の採用フレームと連続していれば即採用。連続しない場合は保留し、
    /// 次フレームが保留と連続したとき（=2連続で整合）に再ロックして採用する。
    /// 孤立したノイズ由来フレームは後続が続かないため決して採用されない。
    /// </summary>
    internal void WriteLtcFrame(TimecodeValue frame)
    {
        long ord = frame.ToOrdinal();

        if (_ltcLastAcceptedOrdinal >= 0 && IsContinuousOrdinal(_ltcLastAcceptedOrdinal, ord))
        {
            _ltcLastAcceptedOrdinal = ord;
            _ltcPendingOrdinal = -1;
            _ltcPendingFrame = null;
            WriteFrame(frame);
            return;
        }

        if (_ltcPendingOrdinal >= 0 && IsContinuousOrdinal(_ltcPendingOrdinal, ord))
        {
            // 2連続で整合したので保留分から順に採用（再ロック）
            var pending = _ltcPendingFrame!.Value;
            _ltcLastAcceptedOrdinal = ord;
            _ltcPendingOrdinal = -1;
            _ltcPendingFrame = null;
            WriteFrame(pending);
            WriteFrame(frame);
            return;
        }

        _ltcPendingOrdinal = ord;
        _ltcPendingFrame = frame;
    }

    // ToOrdinalは1秒=30の固定基準。次フレームとの差は同一秒内で1、
    // 秒境界では 30-(fps-1) となるため、24fps(フレーム23→0)の7までを連続とみなす
    private static bool IsContinuousOrdinal(long previous, long next)
    {
        long diff = next - previous;
        return diff >= 1 && diff <= 7;
    }

    public void StartLtc(string audioDeviceId, bool isLoopback = false)
    {
        var device = FindDevice(audioDeviceId)
            ?? throw new ArgumentException($"オーディオデバイスが見つかりません: {audioDeviceId}", nameof(audioDeviceId));

        StopLtcCapture();

        lock (_lock)
        {
            _session++;
        }

        _ltcAutoDetectActive = true;
        _ltcMaxFrameSeen = 0;
        _ltcDropFrameSeen = false;
        _ltcLastAcceptedOrdinal = -1;
        _ltcPendingOrdinal = -1;
        _ltcPendingFrame = null;
        ActiveSource = TimecodeSourceType.Ltc;

        var decoder = new LtcDecoder();
        decoder.Initialize(SampleRate, FrameRate.FramesPerSecond());
        decoder.FrameDecoded += (_, timecodeValue) => WriteLtcFrame(timecodeValue);
        _ltcDecoder = decoder;

        var capture = _captureFactory();
        capture.AudioSamplesAvailable += OnCaptureSamplesAvailable;
        capture.ErrorOccurred += OnAudioError;
        _capture = capture;

        try
        {
            capture.Start(device);
        }
        catch
        {
            StopLtcCapture();
            throw;
        }
    }

    public void StartGenerator(GeneratorSettings settings)
    {
        Stop();

        lock (_lock)
        {
            _session++;
        }

        FrameRate = settings.FrameRate;
        _ltcAutoDetectActive = false;
        ActiveSource = TimecodeSourceType.Generator;

        var encoder = new LtcEncoder();
        encoder.Initialize(SampleRate, settings.FrameRate);
        encoder.VolumeLevel = settings.VolumeLevel;
        _ltcEncoder = encoder;

        var generator = new TimecodeGenerator();
        generator.FrameGenerated += (_, tc) =>
        {
            WriteFrame(tc);
            _ltcEncoder?.EnqueueFrame(tc);
        };
        _generator = generator;

        var outputDevice = FindDevice(settings.OutputDeviceId);
        if (outputDevice is not null)
        {
            try
            {
                var playback = _playbackFactory();
                playback.Start(outputDevice);
                _playback = playback;
                StartPlaybackFeed(encoder, playback);
            }
            catch (Exception ex)
            {
                // Graceful degradation: generator continues without LTC output
                OnAudioError(this, new AudioErrorEventArgs($"LTC出力デバイスを開けませんでした: {ex.Message}", ex));
                _playback?.Dispose();
                _playback = null;
            }
        }

        generator.Start(settings.StartTime, settings.FrameRate);
    }

    public void ResumeGenerator()
    {
        _generator?.Resume();
    }

    public void StopGenerator()
    {
        // Pause: stop the timer but keep the generator and its position
        lock (_lock)
        {
            _session++;
        }
        _generator?.Stop();
        TransitionToNotReceiving();
    }

    public void ResetGenerator()
    {
        _generator?.Reset();
    }

    public void ResetGenerator(TimecodeValue startTime)
    {
        _generator?.ResetTo(startTime);
    }

    public void Stop()
    {
        // 以降に処理される積み残しフレームを無効化してから状態を落とす
        lock (_lock)
        {
            _session++;
        }

        DisposeGenerator();
        StopLtcCapture();
        TransitionToNotReceiving();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopFreerun();
        DisposeGenerator();
        StopLtcCapture();

        _signalLossTimer.Dispose();
        _channel.Writer.TryComplete();
        _cts.Cancel();
        _workerThread.Join(1000);
        _cts.Dispose();
    }

    private AudioDeviceInfo? FindDevice(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return null;

        return _audioDeviceService.GetCaptureDevices()
            .Concat(_audioDeviceService.GetRenderDevices())
            .FirstOrDefault(d => d.Id == deviceId);
    }

    private void OnCaptureSamplesAvailable(object? sender, AudioSamplesEventArgs e)
    {
        var decoder = _ltcDecoder;
        if (decoder is null) return;

        try
        {
            var samples = e.Samples;
            var buffer = new byte[samples.Length * sizeof(float)];
            Buffer.BlockCopy(samples, 0, buffer, 0, buffer.Length);
            decoder.ProcessSamples(buffer, buffer.Length, SampleRate, bitsPerSample: 32, channels: 1);

            AudioSamplesAvailable?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LTC error: {ex.Message}");
        }
    }

    private void OnAudioError(object? sender, AudioErrorEventArgs e)
    {
        AudioErrorOccurred?.Invoke(this, e);
    }

    private void StartPlaybackFeed(LtcEncoder encoder, IAudioPlayback playback)
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        _playbackFeedCts = cts;

        const int bytesPerSecond = SampleRate * 2;

        var thread = new Thread(() =>
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            long bytesWritten = 0;

            while (!token.IsCancellationRequested)
            {
                long targetBytes = (long)((stopwatch.Elapsed.TotalMilliseconds + PlaybackLeadMs) / 1000.0 * bytesPerSecond);
                targetBytes -= targetBytes % 2;

                int toWrite = (int)(targetBytes - bytesWritten);
                if (toWrite > 0)
                {
                    var buffer = new byte[toWrite];
                    encoder.Read(buffer, 0, toWrite);
                    try
                    {
                        playback.WriteSamples(buffer, 0, toWrite);
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    bytesWritten += toWrite;
                }

                Thread.Sleep(PlaybackFeedIntervalMs);
            }
        })
        {
            Name = "TimecodeEngine-LtcOutput",
            IsBackground = true,
        };
        _playbackFeedThread = thread;
        thread.Start();
    }

    private void StopPlaybackFeed()
    {
        _playbackFeedCts?.Cancel();
        _playbackFeedThread?.Join(500);
        _playbackFeedCts?.Dispose();
        _playbackFeedCts = null;
        _playbackFeedThread = null;
    }

    private void DisposeGenerator()
    {
        if (_generator != null)
        {
            _generator.Stop();
            _generator.Dispose();
            _generator = null;
        }

        StopPlaybackFeed();

        if (_playback != null)
        {
            try { _playback.Stop(); } catch { /* ignore */ }
            _playback.Dispose();
            _playback = null;
        }

        if (_ltcEncoder != null)
        {
            _ltcEncoder.Reset();
            _ltcEncoder = null;
        }
    }

    private void StopLtcCapture()
    {
        if (_capture != null)
        {
            _capture.AudioSamplesAvailable -= OnCaptureSamplesAvailable;
            _capture.ErrorOccurred -= OnAudioError;
            try { _capture.Stop(); } catch { /* ignore if already stopped */ }
            _capture.Dispose();
            _capture = null;
        }

        if (_ltcDecoder != null)
        {
            _ltcDecoder.Dispose();
            _ltcDecoder = null;
        }
    }

    private void WorkerLoop()
    {
        var reader = _channel.Reader;
        var token = _cts.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var waitTask = reader.WaitToReadAsync(token);
                if (!waitTask.IsCompleted)
                {
                    if (!waitTask.AsTask().GetAwaiter().GetResult())
                        break;
                }
                else if (!waitTask.Result)
                {
                    break;
                }

                while (reader.TryRead(out var item))
                {
                    ProcessFrame(item.Session, item.Frame);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ChannelClosedException)
        {
        }
    }

    private void ProcessFrame(int session, TimecodeValue rawFrame)
    {
        if (session != Volatile.Read(ref _session)) return;

        if (_ltcAutoDetectActive)
        {
            if (rawFrame.FrameRate.IsDropFrame())
                _ltcDropFrameSeen = true;
            if (rawFrame.Frames > _ltcMaxFrameSeen)
                _ltcMaxFrameSeen = rawFrame.Frames;

            FrameRate = LtcDecoder.DetermineFrameRate(_ltcMaxFrameSeen, _ltcDropFrameSeen);
        }

        rawFrame = new TimecodeValue(rawFrame.Hours, rawFrame.Minutes, rawFrame.Seconds, rawFrame.Frames, FrameRate);

        TimecodeOffset currentOffset;
        lock (_lock)
        {
            currentOffset = _offset;
        }

        var offsetFrame = rawFrame.Add(currentOffset);

        bool becameReceiving;
        lock (_lock)
        {
            // Stop()と競合した場合は状態を書き換えない（Stopはロック内で世代を進める）
            if (session != _session) return;

            _currentRawTimecode = rawFrame;
            _currentOffsetTimecode = offsetFrame;

            becameReceiving = _isFreerunning || !_isReceiving;
            if (_isFreerunning) StopFreerun();
            _isReceiving = true;
        }

        _signalLossTimer.Change(SignalLossTimeoutMs, Timeout.Infinite);

        if (becameReceiving)
        {
            StatusChanged?.Invoke(this, new TimecodeStatusChangedEventArgs(TimecodeReceiveStatus.Receiving));
        }

        TimecodeUpdated?.Invoke(this, new TimecodeUpdatedEventArgs(rawFrame, offsetFrame));
    }

    private void TransitionToNotReceiving()
    {
        if (!_isReceiving && !_isFreerunning) return;
        StopFreerun();
        _isReceiving = false;
        _signalLossTimer.Change(Timeout.Infinite, Timeout.Infinite);
        StatusChanged?.Invoke(this, new TimecodeStatusChangedEventArgs(TimecodeReceiveStatus.NotReceiving));
    }

    private void OnSignalLossTimeout(object? state)
    {
        if (_disposed) return;

        double freerunDuration;
        TimecodeSourceType source;
        lock (_lock)
        {
            freerunDuration = _freerunDurationSeconds;
            source = _activeSource;
        }

        if (source == TimecodeSourceType.Ltc && freerunDuration > 0)
        {
            StartFreerun(freerunDuration);
        }
        else
        {
            TransitionToNotReceiving();
        }
    }

    private void StartFreerun(double durationSeconds)
    {
        if (_isFreerunning || _disposed) return;

        _isFreerunning = true;
        _isReceiving = false;

        StatusChanged?.Invoke(this, new TimecodeStatusChangedEventArgs(TimecodeReceiveStatus.Freerunning));

        _freerunCts = new CancellationTokenSource();
        var token = _freerunCts.Token;

        var expiryMs = (int)(durationSeconds * 1000);
        _freerunExpiryTimer = new Timer(_ =>
        {
            if (_disposed) return;
            TransitionToNotReceiving();
        }, null, expiryMs, Timeout.Infinite);

        TimecodeValue lastRaw;
        lock (_lock)
        {
            lastRaw = _currentRawTimecode;
        }

        var fps = FrameRate.FramesPerSecond();
        var intervalMs = 1000.0 / fps;
        var lastTotalFrames = lastRaw.TotalFrames();

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

                var rawFrame = TimecodeValue.FromTotalFrames(lastTotalFrames + frameCount, FrameRate);

                TimecodeOffset offset;
                lock (_lock)
                {
                    offset = _offset;
                }
                var offsetFrame = rawFrame.Add(offset);

                lock (_lock)
                {
                    _currentRawTimecode = rawFrame;
                    _currentOffsetTimecode = offsetFrame;
                }

                TimecodeUpdated?.Invoke(this, new TimecodeUpdatedEventArgs(rawFrame, offsetFrame));
            }
        })
        {
            Name = "TimecodeEngine-Freerun",
            IsBackground = true,
        };
        freerunThread.Start();
    }

    private void StopFreerun()
    {
        if (!_isFreerunning) return;

        _freerunCts?.Cancel();
        _freerunCts?.Dispose();
        _freerunCts = null;

        _freerunExpiryTimer?.Dispose();
        _freerunExpiryTimer = null;

        _isFreerunning = false;
    }
}
