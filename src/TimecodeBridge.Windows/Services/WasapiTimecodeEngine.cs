using TimecodeBridge.Core.Services;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Windows.Services;

/// <summary>
/// Windows(WASAPI/NAudio)実装のタイムコードエンジン。実行はWindowsのみ、コンパイルは全OSで可能。
/// </summary>
public class WasapiTimecodeEngine : ITimecodeEngine, IDisposable
{
    private readonly Channel<(int Session, TimecodeValue Frame)> _channel;

    // Start/Stopのたびに増える世代番号。停止前に積まれた古いフレームが
    // 停止後に処理されて受信状態へ戻るのを防ぐ（フレームに世代を付けて破棄する）
    private int _session;
    private readonly CancellationTokenSource _cts;
    private readonly Thread _workerThread;
    private readonly object _lock = new();

    // Signal loss detection
    private readonly Timer _signalLossTimer;
    private const int SignalLossTimeoutMs = 500;

    // LTC capture
    private WasapiCapture? _wasapiCapture;
    private LtcDecoder? _ltcDecoder;
    private readonly ManualResetEventSlim _captureStoppedEvent = new(true);

    // Generator
    private TimecodeGenerator? _generator;
    private LtcEncoder? _ltcEncoder;
    private WasapiOut? _wasapiOut;

    // Freerun state
    private double _freerunDurationSeconds;
    private volatile bool _isFreerunning;
    private CancellationTokenSource? _freerunCts;
    private Timer? _freerunExpiryTimer;

    // LTC frame rate auto-detection
    private bool _ltcAutoDetectActive;
    private int _ltcMaxFrameSeen;
    private bool _ltcDropFrameSeen;

    // Thread-safe state
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

    public WasapiTimecodeEngine(FrameRate frameRate)
    {
        FrameRate = frameRate;
        _offset = TimecodeOffset.Zero(frameRate);

        _channel = Channel.CreateUnbounded<(int Session, TimecodeValue Frame)>(
            new UnboundedChannelOptions { SingleWriter = true });

        _cts = new CancellationTokenSource();
        _signalLossTimer = new Timer(OnSignalLossTimeout, null, Timeout.Infinite, Timeout.Infinite);

        _workerThread = new Thread(WorkerLoop)
        {
            Name = "WasapiTimecodeEngine-Worker",
            IsBackground = true,
        };
        _workerThread.Start();
    }

    /// <summary>
    /// Writes a timecode frame into the channel pipeline.
    /// Called from capture threads (LTC decoder).
    /// </summary>
    internal void WriteFrame(TimecodeValue frame)
    {
        if (_disposed) return;
        _channel.Writer.TryWrite((Volatile.Read(ref _session), frame));
    }

    public void StartLtc(string audioDeviceId, bool isLoopback = false)
    {
        StopLtcCapture();

        lock (_lock)
        {
            _session++;
        }

        _ltcAutoDetectActive = true;
        _ltcMaxFrameSeen = 0;
        _ltcDropFrameSeen = false;
        ActiveSource = TimecodeSourceType.Ltc;

        _ltcDecoder = new LtcDecoder();

        var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(audioDeviceId);
        _wasapiCapture = isLoopback
            ? new WasapiLoopbackCapture(device)
            : new WasapiCapture(device);

        var waveFormat = _wasapiCapture.WaveFormat;
        int fps = FrameRate.FramesPerSecond();
        _ltcDecoder.Initialize(waveFormat.SampleRate, fps);

        int channels = waveFormat.Channels;
        int bitsPerSample = waveFormat.BitsPerSample;
        int sampleRate = waveFormat.SampleRate;

        _ltcDecoder.FrameDecoded += (_, timecodeValue) => WriteFrame(timecodeValue);

        _wasapiCapture.DataAvailable += (_, e) =>
        {
            try
            {
                _ltcDecoder?.ProcessSamples(e.Buffer, e.BytesRecorded, sampleRate, bitsPerSample, channels);

                // Extract mono float samples for waveform display
                if (AudioSamplesAvailable != null)
                {
                    var samples = ExtractMonoSamples(e.Buffer, e.BytesRecorded, bitsPerSample, channels);
                    AudioSamplesAvailable.Invoke(this, new AudioSamplesEventArgs(samples));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LTC error: {ex.Message}");
            }
        };

        _wasapiCapture.RecordingStopped += (_, _) =>
        {
            _captureStoppedEvent.Set();
        };

        _captureStoppedEvent.Reset();
        _wasapiCapture.StartRecording();
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

        _generator = new TimecodeGenerator();
        _ltcEncoder = new LtcEncoder();

        // Initialize LTC encoder and audio output
        const int sampleRate = 48000;
        _ltcEncoder.Initialize(sampleRate, settings.FrameRate);
        _ltcEncoder.VolumeLevel = settings.VolumeLevel;

        _generator.FrameGenerated += (_, tc) =>
        {
            WriteFrame(tc);
            _ltcEncoder?.EnqueueFrame(tc);
        };

        // Try to initialize audio output device
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
                System.Diagnostics.Debug.WriteLine($"LTC output device error: {ex.Message}");
                _wasapiOut?.Dispose();
                _wasapiOut = null;
                // Graceful degradation: generator continues without LTC output
            }
        }

        _generator.Start(settings.StartTime, settings.FrameRate);
    }

    public void ResumeGenerator()
    {
        if (_generator == null) return;

        // Re-initialize LTC output if needed
        if (_ltcEncoder != null && _wasapiOut != null)
        {
            try { _wasapiOut.Play(); } catch { /* ignore */ }
        }

        _generator.Resume();
    }

    public void StopGenerator()
    {
        // Pause: stop the timer but keep the generator and its position
        lock (_lock)
        {
            _session++;
        }
        _generator?.Stop();

        if (_wasapiOut != null)
        {
            try { _wasapiOut.Pause(); } catch { /* ignore */ }
        }

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

        // Wait for the worker thread to finish (with timeout)
        _workerThread.Join(1000);

        _cts.Dispose();
    }

    private void DisposeGenerator()
    {
        if (_generator != null)
        {
            _generator.Stop();
            _generator.Dispose();
            _generator = null;
        }

        if (_wasapiOut != null)
        {
            try { _wasapiOut.Stop(); } catch { /* ignore */ }
            _wasapiOut.Dispose();
            _wasapiOut = null;
        }

        if (_ltcEncoder != null)
        {
            _ltcEncoder.Reset();
            _ltcEncoder = null;
        }
    }

    private void StopLtcCapture()
    {
        if (_wasapiCapture != null)
        {
            try { _wasapiCapture.StopRecording(); } catch { /* ignore if already stopped */ }

            // Wait for capture thread to fully stop before disposing decoder
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

    private void WorkerLoop()
    {
        var reader = _channel.Reader;
        var token = _cts.Token;

        try
        {
            // Use synchronous blocking read pattern for the dedicated worker thread
            while (!token.IsCancellationRequested)
            {
                // WaitToReadAsync returns a ValueTask; we block on it here
                var waitTask = reader.WaitToReadAsync(token);
                if (!waitTask.IsCompleted)
                {
                    // Asynchronously wait, but block the thread
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
            // Normal shutdown
        }
        catch (ChannelClosedException)
        {
            // Channel completed
        }
    }

    private void ProcessFrame(int session, TimecodeValue rawFrame)
    {
        if (session != Volatile.Read(ref _session)) return;

        // Auto-detect frame rate from LTC signal
        if (_ltcAutoDetectActive)
        {
            if (rawFrame.FrameRate.IsDropFrame())
                _ltcDropFrameSeen = true;
            if (rawFrame.Frames > _ltcMaxFrameSeen)
                _ltcMaxFrameSeen = rawFrame.Frames;

            FrameRate = LtcDecoder.DetermineFrameRate(_ltcMaxFrameSeen, _ltcDropFrameSeen);
        }

        // Normalize to engine's FrameRate
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

        // Reset signal loss timer
        _signalLossTimer.Change(SignalLossTimeoutMs, Timeout.Infinite);

        if (becameReceiving)
        {
            StatusChanged?.Invoke(this, new TimecodeStatusChangedEventArgs(TimecodeReceiveStatus.Receiving));
        }

        // Fire timecode updated event
        TimecodeUpdated?.Invoke(this, new TimecodeUpdatedEventArgs(rawFrame, offsetFrame));
    }

    private static float[] ExtractMonoSamples(byte[] buffer, int bytesRecorded, int bitsPerSample, int channels)
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

        // If LTC mode and freerun is enabled, start freerun instead of going to NotReceiving
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

        // Set expiry timer to transition to NotReceiving after duration
        var expiryMs = (int)(durationSeconds * 1000);
        _freerunExpiryTimer = new Timer(_ =>
        {
            if (_disposed) return;
            TransitionToNotReceiving();
        }, null, expiryMs, Timeout.Infinite);

        // Start freerun frame generation on a background thread
        TimecodeValue lastRaw;
        TimecodeOffset currentOffset;
        lock (_lock)
        {
            lastRaw = _currentRawTimecode;
            currentOffset = _offset;
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

                // Spin-wait until it's time for the next frame
                while (stopwatch.Elapsed.TotalMilliseconds < nextFrameTime)
                {
                    if (token.IsCancellationRequested) return;
                    Thread.Sleep(1);
                }

                if (token.IsCancellationRequested) return;

                var newTotalFrames = lastTotalFrames + frameCount;
                var rawFrame = TimecodeValue.FromTotalFrames(newTotalFrames, FrameRate);

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
            Name = "WasapiTimecodeEngine-Freerun",
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
