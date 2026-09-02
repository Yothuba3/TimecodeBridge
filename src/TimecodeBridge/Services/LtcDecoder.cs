using TimecodeBridge.Models;
using TimecodeBridge.Services.Interfaces;

namespace TimecodeBridge.Services;

/// <summary>
/// Pure managed C# LTC (Linear Timecode) decoder.
/// Decodes SMPTE LTC from audio samples using zero-crossing detection
/// and biphase mark coding (BMC) decoding — no native dependencies.
/// </summary>
public class LtcDecoder : ILtcDecoder
{
    // LTC sync word (last 16 bits of each 80-bit frame, stored LSB-first in register)
    // Transmission order: 0011 1111 1111 1101
    // As 16-bit LSB-first value: 0xBFFC
    private const ushort SyncWord = 0xBFFC;

    // 区間長の判定窓は対応する最速レート(30fps)を基準に固定する。
    // 設定レートから決めると、24fps設定では30fpsの「0」ビット(半ビット×2の長さ)が
    // 短区間の上限とちょうど一致して誤分類され、1フレームもデコードできなくなる。
    // 30fps基準の窓は24〜30fpsの全入力に対して十分な余裕がある。
    private const int MaxSupportedFps = 30;

    private int _sampleRate;
    private double _samplesPerBit;
    private bool _initialized;
    private bool _disposed;

    // エッジ検出状態。生の符号反転ではなく、DC成分と振幅包絡を追従させた
    // Schmitt trigger（ヒステリシス）でエッジを取る。DCバイアス＋減衰した弱信号で0を跨がなく
    // なったり、ゼロ近傍ノイズで偽エッジが多発してビット区間が壊れるのを防ぐ。
    private double _dc;          // DC成分の低速IIR推定
    private double _env;         // 振幅包絡の低速IIR推定
    private bool _polarity;      // Schmitt状態: true=正側にいる
    private double _samplesSinceLastCrossing;

    // Biphase decoding state
    private double _shortMin;   // minimum samples for a valid half-bit interval
    private double _shortMax;   // maximum samples for a half-bit interval
    private double _longMax;    // maximum samples for a full-bit interval
    private bool _expectSecondHalf;

    // Sliding 80-bit shift register (continuously checks for sync word)
    private ulong _shiftLo;     // bits 0-63
    private ushort _shiftHi;    // bits 64-79
    private int _totalBits;     // total bits shifted in (for minimum frame detection)

    // チャンネルフレーム（全ch分のサンプル）の途中で切れたバッファの端数を次回へ繰り越す。
    // これをしないと、バッファ長がチャンネルフレーム境界に揃わないとき（ステレオでよく起きる）に
    // デインターリーブの位相が1サンプルずれて固定化し、以降デコードが永久に止まる。
    private readonly byte[] _carry = new byte[16];
    private int _carryLen;

    public event EventHandler<TimecodeValue>? FrameDecoded;

    public LtcDecoder()
    {
    }

    /// <summary>旧呼び出し互換。fps は判定に使わない（<see cref="MaxSupportedFps"/> 参照）。</summary>
    public void Initialize(int sampleRate, int fps) => Initialize(sampleRate);

    public void Initialize(int sampleRate)
    {
        _sampleRate = sampleRate;

        // Each LTC frame is 80 bits. bits per second = 80 * fps
        _samplesPerBit = (double)sampleRate / (80.0 * MaxSupportedFps);

        // Tolerance windows for interval classification
        double halfBit = _samplesPerBit / 2.0;
        _shortMin = halfBit * 0.4;
        _shortMax = halfBit * 1.6;
        _longMax = _samplesPerBit * 1.6;

        _dc = 0;
        _env = 0.1;
        _polarity = false;
        _samplesSinceLastCrossing = 0;
        _expectSecondHalf = false;
        _shiftLo = 0;
        _shiftHi = 0;
        _totalBits = 0;
        _carryLen = 0;
        _initialized = true;
    }

    public void ProcessSamples(byte[] buffer, int bytesRecorded, int sampleRate, int bitsPerSample, int channels)
    {
        if (_disposed || !_initialized) return;
        if (channels < 1) channels = 1;

        int bytesPerSample = bitsPerSample == 32 ? 4 : bitsPerSample == 16 ? 2 : 0;
        if (bytesPerSample == 0) return;

        int frameBytes = bytesPerSample * channels; // 全chを1組とした「チャンネルフレーム」のバイト数
        if (frameBytes > _carry.Length) { _carryLen = 0; return; } // 想定外の巨大ch数は無視

        // 前回の端数（チャンネルフレーム未満のバイト）から、今回の先頭を使って1フレーム分を完成させる。
        // 完成させたら、それを1サンプルとしてデコードし、残りは境界の揃った位置から処理する。
        int offset = 0;
        if (_carryLen > 0)
        {
            int need = frameBytes - _carryLen;
            if (bytesRecorded < need)
            {
                Array.Copy(buffer, 0, _carry, _carryLen, bytesRecorded);
                _carryLen += bytesRecorded;
                return;
            }
            Array.Copy(buffer, 0, _carry, _carryLen, need);
            ProcessOneSample(ReadSample(_carry, 0, bitsPerSample));
            _carryLen = 0;
            offset = need;
        }

        int remaining = bytesRecorded - offset;
        int fullFrames = remaining / frameBytes;
        for (int i = 0; i < fullFrames; i++)
        {
            ProcessOneSample(ReadSample(buffer, offset + i * frameBytes, bitsPerSample));
        }

        // 末尾に残ったチャンネルフレーム未満のバイトを次回へ繰り越す
        int consumed = offset + fullFrames * frameBytes;
        int leftover = bytesRecorded - consumed;
        if (leftover > 0)
        {
            Array.Copy(buffer, consumed, _carry, 0, leftover);
            _carryLen = leftover;
        }
    }

    // チャンネルフレーム先頭（=先頭チャンネル）のサンプルを float で読む
    private static float ReadSample(byte[] buffer, int byteOffset, int bitsPerSample)
    {
        if (bitsPerSample == 32) return BitConverter.ToSingle(buffer, byteOffset);
        short raw = BitConverter.ToInt16(buffer, byteOffset);
        return raw / 32768.0f;
    }

    private void ProcessOneSample(float sample)
    {
        _samplesSinceLastCrossing++;

        // DC成分と振幅包絡を低速IIRで追従し、それに追従したSchmitt trigger（ヒステリシス）で
        // エッジを検出する。ヒステリシス幅は推定振幅の15%（下限0.02）。
        _dc += 0.0005 * (sample - _dc);
        double x = sample - _dc;
        double ax = x < 0 ? -x : x;
        _env += 0.001 * (ax - _env);
        double thresh = Math.Max(0.02, _env * 0.15);

        bool crossed = false;
        if (_polarity)
        {
            if (x < -thresh) { _polarity = false; crossed = true; }
        }
        else
        {
            if (x > thresh) { _polarity = true; crossed = true; }
        }

        if (!crossed) return;

        double interval = _samplesSinceLastCrossing;
        _samplesSinceLastCrossing = 0;

        // Classify interval
        if (interval < _shortMin || interval > _longMax)
        {
            // 範囲外（ノイズ由来の偽区間や欠落）。biphaseの半ビット状態だけ仕切り直し、
            // 80bitシフトレジスタとビット計数は保持する。捨てると、局所グリッチのたびに
            // 再び80bit積み直すまで同期語を認められず、乱れが頻発するとTCが永久に止まる。
            // ガベージはsyncワード16bit一致とBCD範囲チェックが弾くので計数保持で問題ない。
            _expectSecondHalf = false;
            return;
        }

        bool isShort = interval <= _shortMax;

        // Biphase Mark Coding (BMC):
        // - Each bit cell starts with a mandatory transition
        // - '1' bit has an additional mid-cell transition → 2 short intervals
        // - '0' bit has no mid-cell transition → 1 long interval
        if (isShort)
        {
            if (_expectSecondHalf)
            {
                // Second short interval completes a '1' bit
                PushBit(true);
                _expectSecondHalf = false;
            }
            else
            {
                // First short interval — expect the second half
                _expectSecondHalf = true;
            }
        }
        else
        {
            // Long interval = '0' bit (or recovery from misalignment)
            _expectSecondHalf = false;
            PushBit(false);
        }
    }

    private void PushBit(bool bit)
    {
        // Shift the 80-bit register right by 1 (new bit goes into MSB of shiftHi)
        // This means oldest bit falls off the LSB of shiftLo
        _shiftLo = (_shiftLo >> 1) | ((ulong)(_shiftHi & 1) << 63);
        _shiftHi = (ushort)(_shiftHi >> 1);

        if (bit)
        {
            _shiftHi |= (ushort)(1 << 15); // set bit 79 (MSB of shiftHi)
        }

        if (_totalBits < 80) _totalBits++;

        // Check for sync word in the most recent 16 bits (bits 64-79 = shiftHi)
        if (_totalBits >= 80 && _shiftHi == SyncWord)
        {
            EmitFrame();
            // ビット計数は80のまま保持する（0に戻さない）。グリッチ後も次のsyncで即フレーム化できる。
            // 同一フレームの再発火は、次のsyncまで80bit進んで初めて _shiftHi が一致するため起きない。
        }
    }

    private void EmitFrame()
    {
        // The 80-bit frame is in the shift register.
        // shiftLo holds bits 0-63, shiftHi holds bits 64-79 (sync word).
        // Extract BCD-encoded timecode fields from shiftLo:
        //
        //  Bits  0-3:  frame units
        //  Bits  8-9:  frame tens
        //  Bit  10:    drop frame flag
        //  Bits 16-19: seconds units
        //  Bits 24-26: seconds tens
        //  Bits 32-35: minutes units
        //  Bits 40-42: minutes tens
        //  Bits 48-51: hours units
        //  Bits 56-57: hours tens

        int frameUnits = (int)(_shiftLo & 0x0F);
        int frameTens = (int)((_shiftLo >> 8) & 0x03);
        bool dropFrame = ((_shiftLo >> 10) & 1) == 1;
        int secUnits = (int)((_shiftLo >> 16) & 0x0F);
        int secTens = (int)((_shiftLo >> 24) & 0x07);
        int minUnits = (int)((_shiftLo >> 32) & 0x0F);
        int minTens = (int)((_shiftLo >> 40) & 0x07);
        int hrUnits = (int)((_shiftLo >> 48) & 0x0F);
        int hrTens = (int)((_shiftLo >> 56) & 0x03);

        int frames = frameTens * 10 + frameUnits;
        int seconds = secTens * 10 + secUnits;
        int minutes = minTens * 10 + minUnits;
        int hours = hrTens * 10 + hrUnits;

        // Sanity check
        if (hours > 23 || minutes > 59 || seconds > 59 || frames >= 30)
            return;

        FrameRate frameRate;
        if (dropFrame)
        {
            frameRate = FrameRate.Fps2997Drop;
        }
        else
        {
            frameRate = frames switch
            {
                < 24 => FrameRate.Fps24,
                < 25 => FrameRate.Fps25,
                _ => FrameRate.Fps30,
            };
        }

        var timecodeValue = new TimecodeValue(hours, minutes, seconds, frames, frameRate);
        FrameDecoded?.Invoke(this, timecodeValue);
    }

    // Static conversion methods kept for test compatibility
    internal static byte[] ConvertToLtcSamples(byte[] buffer, int bytesRecorded, int bitsPerSample, int channels)
    {
        if (bitsPerSample == 32)
            return ConvertFloat32ToU8(buffer, bytesRecorded, channels);
        else if (bitsPerSample == 16)
            return ConvertPcm16ToU8(buffer, bytesRecorded, channels);
        return Array.Empty<byte>();
    }

    internal static byte[] ConvertFloat32ToU8(byte[] buffer, int bytesRecorded, int channels)
    {
        int totalSamples = bytesRecorded / 4;
        int framesToProcess = totalSamples / channels;
        byte[] result = new byte[framesToProcess];
        for (int i = 0; i < framesToProcess; i++)
        {
            float sample = BitConverter.ToSingle(buffer, i * channels * 4);
            sample = Math.Clamp(sample, -1.0f, 1.0f);
            result[i] = (byte)((sample * 127.0f) + 128.0f);
        }
        return result;
    }

    internal static byte[] ConvertPcm16ToU8(byte[] buffer, int bytesRecorded, int channels)
    {
        int totalSamples = bytesRecorded / 2;
        int framesToProcess = totalSamples / channels;
        byte[] result = new byte[framesToProcess];
        for (int i = 0; i < framesToProcess; i++)
        {
            short sample = BitConverter.ToInt16(buffer, i * channels * 2);
            result[i] = (byte)((sample + 32768) >> 8);
        }
        return result;
    }

    public static FrameRate DetermineFrameRate(int maxFrameNumber, bool dropFrame)
    {
        if (dropFrame) return FrameRate.Fps2997Drop;
        return maxFrameNumber switch
        {
            < 24 => FrameRate.Fps24,
            < 25 => FrameRate.Fps25,
            _ => FrameRate.Fps30,
        };
    }

    internal static TimecodeValue ToTimecodeValue(int hours, int minutes, int seconds, int frames, FrameRate frameRate)
    {
        return new TimecodeValue(hours, minutes, seconds, frames, frameRate);
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
