using System.Collections.Concurrent;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Core.Services;

public class LtcEncoder : ILtcEncoder
{
    private int _sampleRate;
    private FrameRate _frameRate;
    private double _samplesPerBit;
    private float _volumeLevel = 0.8f;
    private readonly ConcurrentQueue<byte[]> _frameQueue = new();
    private byte[]? _currentBuffer;
    private int _bufferOffset;
    private Models.WaveFormat _waveFormat = new(48000, 16, 1);
    private bool _lastBmcLevel;
    private bool _initialized;

    public float VolumeLevel
    {
        get => _volumeLevel;
        set => _volumeLevel = Math.Clamp(value, 0f, 1f);
    }

    public Models.WaveFormat WaveFormat => _waveFormat;

    public void Initialize(int sampleRate, FrameRate frameRate)
    {
        _sampleRate = sampleRate;
        _frameRate = frameRate;
        int fps = frameRate.FramesPerSecond();
        _samplesPerBit = sampleRate / (80.0 * fps);
        _waveFormat = new Models.WaveFormat(sampleRate, 16, 1);

        while (_frameQueue.TryDequeue(out _)) { }
        _currentBuffer = null;
        _bufferOffset = 0;
        _lastBmcLevel = false;
        _initialized = true;
    }

    public void EnqueueFrame(TimecodeValue frame)
    {
        if (!_initialized)
            throw new InvalidOperationException("LtcEncoder is not initialized.");

        var (lo, hi) = EncodeFrame(frame);
        byte[] pcmBuffer = GenerateBmcAudio(lo, hi);
        _frameQueue.Enqueue(pcmBuffer);
    }

    public void Reset()
    {
        while (_frameQueue.TryDequeue(out _)) { }
        _currentBuffer = null;
        _bufferOffset = 0;
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        int written = 0;

        while (written < count)
        {
            // Try to consume from current buffer
            if (_currentBuffer != null && _bufferOffset < _currentBuffer.Length)
            {
                int available = _currentBuffer.Length - _bufferOffset;
                int toCopy = Math.Min(available, count - written);
                Buffer.BlockCopy(_currentBuffer, _bufferOffset, buffer, offset + written, toCopy);
                _bufferOffset += toCopy;
                written += toCopy;

                if (_bufferOffset >= _currentBuffer.Length)
                {
                    _currentBuffer = null;
                    _bufferOffset = 0;
                }

                continue;
            }

            // Try to dequeue next frame buffer
            if (_frameQueue.TryDequeue(out byte[]? nextBuffer))
            {
                _currentBuffer = nextBuffer;
                _bufferOffset = 0;
                continue;
            }

            // No more data — fill remaining with silence
            Array.Clear(buffer, offset + written, count - written);
            written = count;
        }

        return count;
    }

    private (ulong lo, ushort hi) EncodeFrame(TimecodeValue frame)
    {
        ulong lo = 0;

        // Bits 0-3: frame units (BCD)
        lo |= (ulong)(frame.Frames % 10) & 0x0F;
        // Bits 8-9: frame tens (BCD)
        lo |= ((ulong)(frame.Frames / 10) & 0x03) << 8;
        // Bit 10: drop frame flag
        if (frame.FrameRate.IsDropFrame())
            lo |= 1UL << 10;
        // Bits 16-19: seconds units (BCD)
        lo |= ((ulong)(frame.Seconds % 10) & 0x0F) << 16;
        // Bits 24-26: seconds tens (BCD)
        lo |= ((ulong)(frame.Seconds / 10) & 0x07) << 24;
        // Bits 32-35: minutes units (BCD)
        lo |= ((ulong)(frame.Minutes % 10) & 0x0F) << 32;
        // Bits 40-42: minutes tens (BCD)
        lo |= ((ulong)(frame.Minutes / 10) & 0x07) << 40;
        // Bits 48-51: hours units (BCD)
        lo |= ((ulong)(frame.Hours % 10) & 0x0F) << 48;
        // Bits 56-57: hours tens (BCD)
        lo |= ((ulong)(frame.Hours / 10) & 0x03) << 56;

        ushort hi = 0xBFFC;

        return (lo, hi);
    }

    private byte[] GenerateBmcAudio(ulong lo, ushort hi)
    {
        int totalSamples = (int)Math.Round(80 * _samplesPerBit);
        byte[] buffer = new byte[totalSamples * 2]; // 16-bit PCM = 2 bytes per sample

        short positiveAmplitude = (short)(32767 * _volumeLevel);
        short negativeAmplitude = (short)(-32767 * _volumeLevel);

        bool level = _lastBmcLevel;

        for (int bit = 0; bit < 80; bit++)
        {
            // Get the current bit value
            bool bitValue;
            if (bit < 64)
                bitValue = ((lo >> bit) & 1) == 1;
            else
                bitValue = ((hi >> (bit - 64)) & 1) == 1;

            // Calculate sample boundaries for this bit cell
            double cellStart = bit * _samplesPerBit;
            double cellMid = (bit + 0.5) * _samplesPerBit;
            double cellEnd = (bit + 1) * _samplesPerBit;

            int sampleStart = (int)Math.Round(cellStart);
            int sampleMid = (int)Math.Round(cellMid);
            int sampleEnd = (int)Math.Round(cellEnd);

            // Clamp to buffer size
            if (sampleEnd > totalSamples) sampleEnd = totalSamples;
            if (sampleMid > totalSamples) sampleMid = totalSamples;

            // Transition at start of bit cell
            level = !level;

            // Write first half of bit cell
            short amplitude = level ? positiveAmplitude : negativeAmplitude;
            for (int s = sampleStart; s < (bitValue ? sampleMid : sampleEnd); s++)
            {
                int bytePos = s * 2;
                if (bytePos + 1 < buffer.Length)
                {
                    buffer[bytePos] = (byte)(amplitude & 0xFF);
                    buffer[bytePos + 1] = (byte)((amplitude >> 8) & 0xFF);
                }
            }

            // For bit '1': transition at mid-cell
            if (bitValue)
            {
                level = !level;
                amplitude = level ? positiveAmplitude : negativeAmplitude;
                for (int s = sampleMid; s < sampleEnd; s++)
                {
                    int bytePos = s * 2;
                    if (bytePos + 1 < buffer.Length)
                    {
                        buffer[bytePos] = (byte)(amplitude & 0xFF);
                        buffer[bytePos + 1] = (byte)((amplitude >> 8) & 0xFF);
                    }
                }
            }
        }

        _lastBmcLevel = level;
        return buffer;
    }
}
