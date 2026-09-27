using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using static TimecodeBridge.Ltc.LibltcNative;

namespace TimecodeBridge.Ltc;

/// <summary>
/// libltc による LTC 復号。入力バッファを先頭チャンネルの float モノラルへ揃えて libltc に渡し、
/// 確定したフレームごとに <see cref="FrameDecoded"/> を発火する(呼び出し元=音声スレッド上)。
/// </summary>
public sealed unsafe class LibltcDecoder : ILtcDecoder
{
    // apv(1 映像フレームあたりの音声サンプル数)は libltc の初期推定にだけ使われ、以後は信号から追従する
    private const int InitialFps = 30;
    private const int QueueSize = 32;

    private readonly object _lock = new();
    private IntPtr _decoder;
    private long _position;
    private int _maxFrameNumber;
    private float[] _mono = new float[4096];
    private bool _disposed;

    public event EventHandler<TimecodeValue>? FrameDecoded;

    public bool IsInitialized => _decoder != IntPtr.Zero;

    /// <summary>直近に確定したフレームの信号レベル(dBFS)。未受信なら null。</summary>
    public double? LastVolumeDbfs { get; private set; }

    public void Initialize(int sampleRate)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            FreeDecoder();
            _decoder = ltc_decoder_create(sampleRate / InitialFps, QueueSize);
            if (_decoder == IntPtr.Zero) throw new InvalidOperationException("ltc_decoder_create が失敗しました");
            _position = 0;
            _maxFrameNumber = 0;
            LastVolumeDbfs = null;
        }
    }

    public void ProcessSamples(byte[] buffer, int bytesRecorded, int sampleRate, int bitsPerSample, int channels, bool is32BitFloat = true)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (channels < 1) channels = 1;
        if (bytesRecorded <= 0) return;
        if (bytesRecorded > buffer.Length) bytesRecorded = buffer.Length;

        int bytesPerSample = bitsPerSample == 32 ? 4 : bitsPerSample == 16 ? 2 : 0;
        if (bytesPerSample == 0) return;

        // チャンネルフレーム(全 ch を 1 組)未満の端数は捨てる。LTC の復号には 1 サンプル未満の欠けは影響しない
        int frameBytes = bytesPerSample * channels;
        int frames = bytesRecorded / frameBytes;
        if (frames == 0) return;

        lock (_lock)
        {
            if (_disposed || _decoder == IntPtr.Zero) return;

            if (_mono.Length < frames) _mono = new float[Math.Max(frames, _mono.Length * 2)];
            var span = buffer.AsSpan(0, frames * frameBytes);
            for (int i = 0; i < frames; i++)
            {
                int offset = i * frameBytes;
                float sample = bitsPerSample == 16
                    ? BitConverter.ToInt16(span.Slice(offset, 2)) / 32768f
                    : is32BitFloat
                        ? BitConverter.ToSingle(span.Slice(offset, 4))
                        : BitConverter.ToInt32(span.Slice(offset, 4)) / 2147483648f;
                // libltc は float を 8bit 整数へキャストするため、非有限値と ±1 超えは渡せない(未定義動作・桁あふれ)
                if (!float.IsFinite(sample)) sample = 0f;
                _mono[i] = Math.Clamp(sample, -1f, 1f);
            }

            fixed (float* p = _mono)
            {
                ltc_decoder_write_float(_decoder, p, (nuint)frames, _position);
            }
            _position += frames;

            LTCFrameExt ext;
            while (ltc_decoder_read(_decoder, &ext) > 0)
            {
                SMPTETimecode st;
                ltc_frame_to_time(&st, &ext.Frame, 0);
                LastVolumeDbfs = ext.Volume;

                bool dropFrame = ext.Frame.DropFrame;
                if (st.Frame > _maxFrameNumber) _maxFrameNumber = st.Frame;
                var frameRate = LtcDecoder.DetermineFrameRate(_maxFrameNumber, dropFrame);
                FrameDecoded?.Invoke(this, new TimecodeValue(st.Hours, st.Mins, st.Secs, st.Frame, frameRate));
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            FreeDecoder();
        }
    }

    private void FreeDecoder()
    {
        if (_decoder == IntPtr.Zero) return;
        ltc_decoder_free(_decoder);
        _decoder = IntPtr.Zero;
    }
}
