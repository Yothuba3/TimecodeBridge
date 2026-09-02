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

    private int _sampleRate;
    private int _fps;
    private double _samplesPerBit;
    private bool _initialized;
    private bool _disposed;

    // Zero-crossing detection state
    private float _prevSample;
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

    public event EventHandler<TimecodeValue>? FrameDecoded;

    public LtcDecoder()
    {
    }

    public void Initialize(int sampleRate, int fps)
    {
        _sampleRate = sampleRate;
        _fps = fps;

        // Each LTC frame is 80 bits. bits per second = 80 * fps
        _samplesPerBit = (double)sampleRate / (80.0 * fps);

        // Tolerance windows for interval classification
        double halfBit = _samplesPerBit / 2.0;
        _shortMin = halfBit * 0.4;
        _shortMax = halfBit * 1.6;
        _longMax = _samplesPerBit * 1.6;

        _prevSample = 0;
        _samplesSinceLastCrossing = 0;
        _expectSecondHalf = false;
        _shiftLo = 0;
        _shiftHi = 0;
        _totalBits = 0;
        _initialized = true;
    }

    public void ProcessSamples(byte[] buffer, int bytesRecorded, int sampleRate, int bitsPerSample, int channels)
    {
        if (_disposed || !_initialized) return;

        if (bitsPerSample == 32)
        {
            ProcessFloat32(buffer, bytesRecorded, channels);
        }
        else if (bitsPerSample == 16)
        {
            ProcessPcm16(buffer, bytesRecorded, channels);
        }
    }

    private void ProcessFloat32(byte[] buffer, int bytesRecorded, int channels)
    {
        int totalSamples = bytesRecorded / 4;
        int frames = totalSamples / channels;

        for (int i = 0; i < frames; i++)
        {
            float sample = BitConverter.ToSingle(buffer, i * channels * 4);
            ProcessOneSample(sample);
        }
    }

    private void ProcessPcm16(byte[] buffer, int bytesRecorded, int channels)
    {
        int totalSamples = bytesRecorded / 2;
        int frames = totalSamples / channels;

        for (int i = 0; i < frames; i++)
        {
            short raw = BitConverter.ToInt16(buffer, i * channels * 2);
            float sample = raw / 32768.0f;
            ProcessOneSample(sample);
        }
    }

    private void ProcessOneSample(float sample)
    {
        _samplesSinceLastCrossing++;

        // Detect zero crossing
        bool crossed = (_prevSample >= 0 && sample < 0) || (_prevSample < 0 && sample >= 0);
        _prevSample = sample;

        if (!crossed) return;

        double interval = _samplesSinceLastCrossing;
        _samplesSinceLastCrossing = 0;

        // Classify interval
        if (interval < _shortMin || interval > _longMax)
        {
            // Out of range — noise or silence, reset biphase state.
            // ビット計数も仕切り直し、乱れを含む80bit窓からフレームを切り出さない
            // （無信号復帰時にノイズと実信号が混ざったガベージフレームが出るのを防ぐ）
            _expectSecondHalf = false;
            _totalBits = 0;
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

        _totalBits++;

        // Check for sync word in the most recent 16 bits (bits 64-79 = shiftHi)
        if (_totalBits >= 80 && _shiftHi == SyncWord)
        {
            EmitFrame();
            _totalBits = 0; // prevent re-triggering on the same frame
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

    internal static FrameRate DetermineFrameRate(int maxFrameNumber, bool dropFrame)
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
