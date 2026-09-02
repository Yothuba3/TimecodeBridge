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
    public void 信号喪失後に別セクションのLTCが来ても受信し直せる()
    {
        static float[] Section(int startSec, int frames)
        {
            var e = new LtcEncoder();
            e.Initialize(48000, FrameRate.Fps30);
            for (int i = 0; i < frames; i++) e.EnqueueFrame(new TimecodeValue(10, 0, startSec, i % 30, FrameRate.Fps30));
            return ReadAllAsFloat(e, frames);
        }

        _engine.StartLtc(InputDevice.Id);

        // セクション1を受信
        _capture.Feed(Section(0, 10));
        Assert.True(SpinWait.SpinUntil(() => _engine.IsReceiving, TimeSpan.FromSeconds(5)), "セクション1を受信できなかった");

        // 無信号を挟んで信号喪失に遷移させる
        Assert.True(SpinWait.SpinUntil(() => !_engine.IsReceiving, TimeSpan.FromSeconds(5)), "信号喪失に遷移しなかった");
        Thread.Sleep(200);

        // 別セクション2が到達 → デコーダが自力で受信し直せること
        var received2 = new ManualResetEventSlim();
        _engine.TimecodeUpdated += (_, e) => { if (e.RawTimecode.Seconds == 40) received2.Set(); };
        _capture.Feed(Section(40, 30));

        Assert.True(received2.Wait(TimeSpan.FromSeconds(5)), "信号喪失後、別セクションのLTCを受信できなかった");
        Assert.True(_engine.IsReceiving);
    }

    [Fact]
    public void 孤立したノイズ由来フレームは採用されない()
    {
        var updates = 0;
        _engine.TimecodeUpdated += (_, _) => Interlocked.Increment(ref updates);

        _engine.StartLtc(InputDevice.Id);

        // 連続しない単発フレーム（ノイズが偶然フレームとして解析されたケース）
        _engine.WriteLtcFrame(new TimecodeValue(3, 3, 3, 3, FrameRate.Fps30));
        Thread.Sleep(200);

        Assert.Equal(0, updates);
        Assert.False(_engine.IsReceiving);
    }

    [Fact]
    public void ノイズ音声の後でも実信号を正しく解析する()
    {
        var decoded = new List<TimecodeValue>();
        var received = new ManualResetEventSlim();
        _engine.TimecodeUpdated += (_, e) =>
        {
            lock (decoded) decoded.Add(e.RawTimecode);
            received.Set();
        };

        _engine.StartLtc(InputDevice.Id);

        // 長時間の無信号を模したノイズ（ランダム波形）を流す
        var rng = new Random(12345);
        var noise = new float[48000 * 2];
        for (int i = 0; i < noise.Length; i++) noise[i] = (float)(rng.NextDouble() * 2 - 1) * 0.3f;
        _capture.Feed(noise);

        // その後に実信号
        var encoder = new LtcEncoder();
        encoder.Initialize(48000, FrameRate.Fps30);
        var expected = new HashSet<long>();
        for (int i = 0; i < 8; i++)
        {
            var tc = new TimecodeValue(10, 20, 30, i, FrameRate.Fps30);
            expected.Add(tc.ToOrdinal());
            encoder.EnqueueFrame(tc);
        }
        _capture.Feed(ReadAllAsFloat(encoder, frameCount: 8));

        Assert.True(received.Wait(TimeSpan.FromSeconds(3)), "実信号がデコードされなかった");
        SpinWait.SpinUntil(() => { lock (decoded) return decoded.Count >= 3; }, TimeSpan.FromSeconds(2));

        lock (decoded)
        {
            Assert.NotEmpty(decoded);
            // ノイズ由来のガベージが混ざらず、送った実フレームのみが採用される
            Assert.All(decoded, tc => Assert.Contains(tc.ToOrdinal(), expected));
        }
    }

    [Fact]
    public void 信号が途切れて別位置から再開しても2フレームで再ロックする()
    {
        var decoded = new List<TimecodeValue>();
        _engine.TimecodeUpdated += (_, e) => { lock (decoded) decoded.Add(e.RawTimecode); };

        _engine.StartLtc(InputDevice.Id);

        // 位置Aで2フレーム → 大きく離れた位置Bで2フレーム
        _engine.WriteLtcFrame(new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30));
        _engine.WriteLtcFrame(new TimecodeValue(1, 0, 0, 1, FrameRate.Fps30));
        _engine.WriteLtcFrame(new TimecodeValue(5, 0, 0, 10, FrameRate.Fps30));
        _engine.WriteLtcFrame(new TimecodeValue(5, 0, 0, 11, FrameRate.Fps30));

        SpinWait.SpinUntil(() => { lock (decoded) return decoded.Count >= 4; }, TimeSpan.FromSeconds(2));

        lock (decoded)
        {
            // フレームレートは自動判定で正規化されるため位置（序数）で比較する
            Assert.Equal(4, decoded.Count);
            Assert.Equal(new TimecodeValue(5, 0, 0, 11, FrameRate.Fps30).ToOrdinal(), decoded[^1].ToOrdinal());
        }
    }

    [Fact]
    public void Stop_積み残しフレームが停止後に処理されても受信状態へ戻らない()
    {
        _engine.FreerunDurationSeconds = 5;
        _engine.StartLtc(InputDevice.Id);

        var encoder = new LtcEncoder();
        encoder.Initialize(48000, FrameRate.Fps30);
        for (int i = 0; i < 6; i++) encoder.EnqueueFrame(new TimecodeValue(0, 0, 2, i, FrameRate.Fps30));

        // フレーム投入直後に停止し、チャネルに積まれた残りをワーカーが後処理する状況を作る
        _capture.Feed(ReadAllAsFloat(encoder, frameCount: 6));
        _engine.Stop();

        // 信号喪失タイマー(500ms)経過後も停止のまま（旧実装では復帰→フリーラン開始していた）
        Thread.Sleep(800);
        Assert.False(_engine.IsReceiving);
        Assert.False(_engine.IsFreerunning);
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
