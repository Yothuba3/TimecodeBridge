using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.App.Services;

namespace TimecodeBridge.App.Tests.Services;

public class TimecodeEngineTests : IDisposable
{
    private static readonly AudioDeviceInfo InputDevice = new("11", "Test Input", false);
    private static readonly AudioDeviceInfo OutputDevice = new("22", "Test Output", false);

    private readonly FakeAudioCapture _capture = new();
    private readonly FakeAudioPlayback _playback = new();
    private readonly TimecodeEngine _engine;

    public TimecodeEngineTests()
    {
        _engine = new TimecodeEngine(
            FrameRate.Fps30,
            new FakeAudioDeviceService([InputDevice], [OutputDevice]),
            () => _capture,
            () => _playback);
    }

    public void Dispose() => _engine.Dispose();

    [Fact]
    public void StartLtc_未知のデバイスIDで例外を投げる()
    {
        Assert.Throws<ArgumentException>(() => _engine.StartLtc("no-such-device"));
    }

    [Fact]
    public void StartLtc_選択したデバイスでキャプチャを開始する()
    {
        _engine.StartLtc(InputDevice.Id);

        Assert.Equal(InputDevice, _capture.StartedDevice);
        Assert.Equal(TimecodeSourceType.Ltc, _engine.ActiveSource);
    }

    [Fact]
    public void StartLtc_キャプチャ音声からLTCをデコードしてTimecodeUpdatedを発火する()
    {
        var received = new ManualResetEventSlim();
        TimecodeValue? decoded = null;
        _engine.TimecodeUpdated += (_, e) =>
        {
            decoded = e.RawTimecode;
            received.Set();
        };

        var statuses = new List<TimecodeReceiveStatus>();
        _engine.StatusChanged += (_, e) => statuses.Add(e.Status);

        _engine.StartLtc(InputDevice.Id);

        var encoder = new LtcEncoder();
        encoder.Initialize(48000, FrameRate.Fps30);
        var frame = new TimecodeValue(1, 2, 3, 4, FrameRate.Fps30);
        for (int i = 0; i < 6; i++)
        {
            encoder.EnqueueFrame(new TimecodeValue(1, 2, 3, 4 + i, FrameRate.Fps30));
        }
        _capture.Feed(ReadAllAsFloat(encoder, frameCount: 6));

        Assert.True(received.Wait(TimeSpan.FromSeconds(2)), "TimecodeUpdated が発火しなかった");
        Assert.NotNull(decoded);
        Assert.Equal(frame.Hours, decoded.Value.Hours);
        Assert.Equal(frame.Minutes, decoded.Value.Minutes);
        Assert.Equal(frame.Seconds, decoded.Value.Seconds);
        Assert.True(_engine.IsReceiving);
        Assert.Contains(TimecodeReceiveStatus.Receiving, statuses);
    }

    [Fact]
    public void Stop_LTC受信中に停止するとキャプチャを解放しNotReceivingになる()
    {
        var notReceiving = new ManualResetEventSlim();
        _engine.StatusChanged += (_, e) =>
        {
            if (e.Status == TimecodeReceiveStatus.NotReceiving) notReceiving.Set();
        };

        _engine.StartLtc(InputDevice.Id);
        var encoder = new LtcEncoder();
        encoder.Initialize(48000, FrameRate.Fps30);
        for (int i = 0; i < 6; i++) encoder.EnqueueFrame(new TimecodeValue(0, 0, 1, i, FrameRate.Fps30));
        _capture.Feed(ReadAllAsFloat(encoder, frameCount: 6));
        Assert.True(SpinWait.SpinUntil(() => _engine.IsReceiving, TimeSpan.FromSeconds(5)), "受信状態にならなかった");

        _engine.Stop();

        Assert.True(_capture.IsStopped);
        Assert.True(_capture.IsDisposed);
        Assert.False(_engine.IsReceiving);
        Assert.True(notReceiving.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void StartGenerator_フレームを生成しLTC音声を出力デバイスに書き込む()
    {
        var received = new ManualResetEventSlim();
        _engine.TimecodeUpdated += (_, _) => received.Set();

        _engine.StartGenerator(new GeneratorSettings
        {
            FrameRate = FrameRate.Fps25,
            StartTime = new TimecodeValue(10, 0, 0, 0, FrameRate.Fps25),
            OutputDeviceId = OutputDevice.Id,
        });

        Assert.True(received.Wait(TimeSpan.FromSeconds(2)), "TimecodeUpdated が発火しなかった");
        Assert.Equal(TimecodeSourceType.Generator, _engine.ActiveSource);
        Assert.Equal(FrameRate.Fps25, _engine.FrameRate);
        Assert.Equal(10, _engine.CurrentRawTimecode.Hours);
        Assert.Equal(OutputDevice, _playback.StartedDevice);
        Assert.True(SpinWait.SpinUntil(() => _playback.BytesWritten > 0, TimeSpan.FromSeconds(2)), "LTC音声が書き込まれなかった");

        _engine.Stop();

        Assert.True(_playback.IsStopped);
        Assert.True(_playback.IsDisposed);
        Assert.False(_engine.IsReceiving);
    }

    [Fact]
    public void StartGenerator_出力デバイス未指定でもフレームを生成する()
    {
        var received = new ManualResetEventSlim();
        _engine.TimecodeUpdated += (_, _) => received.Set();

        _engine.StartGenerator(new GeneratorSettings { FrameRate = FrameRate.Fps30 });

        Assert.True(received.Wait(TimeSpan.FromSeconds(2)));
        Assert.Null(_playback.StartedDevice);
    }

    [Fact]
    public void StartGenerator_出力デバイスが開けない場合はエラー通知して生成を継続する()
    {
        _playback.ThrowOnStart = true;
        AudioErrorEventArgs? error = null;
        _engine.AudioErrorOccurred += (_, e) => error = e;
        var received = new ManualResetEventSlim();
        _engine.TimecodeUpdated += (_, _) => received.Set();

        _engine.StartGenerator(new GeneratorSettings { OutputDeviceId = OutputDevice.Id });

        Assert.NotNull(error);
        Assert.True(received.Wait(TimeSpan.FromSeconds(2)));
    }

    private static float[] ReadAllAsFloat(LtcEncoder encoder, int frameCount)
    {
        int samplesPerFrame = (int)Math.Round(48000.0 / 30);
        var bytes = new byte[samplesPerFrame * 2 * frameCount];
        encoder.Read(bytes, 0, bytes.Length);

        var samples = new float[bytes.Length / 2];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
        }
        return samples;
    }

    private sealed class FakeAudioDeviceService(IReadOnlyList<AudioDeviceInfo> capture, IReadOnlyList<AudioDeviceInfo> render) : IAudioDeviceService
    {
        public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices() => capture;
        public IReadOnlyList<AudioDeviceInfo> GetRenderDevices() => render;
    }

    private sealed class FakeAudioCapture : IAudioCapture
    {
        public AudioDeviceInfo? StartedDevice { get; private set; }
        public bool IsStopped { get; private set; }
        public bool IsDisposed { get; private set; }

        public event EventHandler<AudioSamplesEventArgs>? AudioSamplesAvailable;
        public event EventHandler<AudioErrorEventArgs>? ErrorOccurred;

        public void Start(AudioDeviceInfo device) => StartedDevice = device;
        public void Stop() => IsStopped = true;
        public void Dispose() => IsDisposed = true;

        public void Feed(float[] samples) => AudioSamplesAvailable?.Invoke(this, new AudioSamplesEventArgs(samples));
        public void RaiseError(string message) => ErrorOccurred?.Invoke(this, new AudioErrorEventArgs(message));
    }

    private sealed class FakeAudioPlayback : IAudioPlayback
    {
        private long _bytesWritten;

        public AudioDeviceInfo? StartedDevice { get; private set; }
        public bool IsStopped { get; private set; }
        public bool IsDisposed { get; private set; }
        public bool ThrowOnStart { get; set; }
        public long BytesWritten => Interlocked.Read(ref _bytesWritten);

        public void Start(AudioDeviceInfo device)
        {
            if (ThrowOnStart) throw new InvalidOperationException("cannot open device");
            StartedDevice = device;
        }

        public void Stop() => IsStopped = true;
        public void WriteSamples(byte[] samples, int offset, int count) => Interlocked.Add(ref _bytesWritten, count);
        public void Dispose() => IsDisposed = true;
    }
}
