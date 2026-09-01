using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
namespace TimecodeBridge.Tests.Services;

using System.Runtime.InteropServices;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Native;
using TimecodeBridge.Services;
using TimecodeBridge.Services.Interfaces;

public class LtcDecoderTests
{
    /// <summary>
    /// Helper: create an IntPtr from byte values (simulating LTCFrame bytes) for Extract tests.
    /// </summary>
    private static (IntPtr ptr, Action free) MakeFramePtr(
        byte b0 = 0, byte b1 = 0, byte b2 = 0, byte b3 = 0,
        byte b4 = 0, byte b5 = 0, byte b6 = 0, byte b7 = 0)
    {
        var ptr = Marshal.AllocHGlobal(LtcFrameHelper.FrameExtBufferSize);
        // Zero out
        for (int i = 0; i < LtcFrameHelper.FrameExtBufferSize; i++)
            Marshal.WriteByte(ptr, i, 0);
        Marshal.WriteByte(ptr, 0, b0);
        Marshal.WriteByte(ptr, 1, b1);
        Marshal.WriteByte(ptr, 2, b2);
        Marshal.WriteByte(ptr, 3, b3);
        Marshal.WriteByte(ptr, 4, b4);
        Marshal.WriteByte(ptr, 5, b5);
        Marshal.WriteByte(ptr, 6, b6);
        Marshal.WriteByte(ptr, 7, b7);
        return (ptr, () => Marshal.FreeHGlobal(ptr));
    }
    // ---------------------------------------------------------------
    // ConvertFloat32ToU8 tests
    // ---------------------------------------------------------------

    [Fact]
    public void ConvertFloat32ToU8_SilenceConvertsToCenter128()
    {
        // 0.0f in IEEE float
        byte[] buffer = BitConverter.GetBytes(0.0f);

        byte[] result = LtcDecoder.ConvertFloat32ToU8(buffer, buffer.Length, 1);

        Assert.Single(result);
        Assert.Equal(128, result[0]);
    }

    [Fact]
    public void ConvertFloat32ToU8_PositiveOneConvertsTo255()
    {
        byte[] buffer = BitConverter.GetBytes(1.0f);

        byte[] result = LtcDecoder.ConvertFloat32ToU8(buffer, buffer.Length, 1);

        Assert.Single(result);
        Assert.Equal(255, result[0]);
    }

    [Fact]
    public void ConvertFloat32ToU8_NegativeOneConvertsTo1()
    {
        byte[] buffer = BitConverter.GetBytes(-1.0f);

        byte[] result = LtcDecoder.ConvertFloat32ToU8(buffer, buffer.Length, 1);

        Assert.Single(result);
        Assert.Equal(1, result[0]);
    }

    [Fact]
    public void ConvertFloat32ToU8_ClampsValuesAboveOne()
    {
        byte[] buffer = BitConverter.GetBytes(2.5f);

        byte[] result = LtcDecoder.ConvertFloat32ToU8(buffer, buffer.Length, 1);

        Assert.Single(result);
        Assert.Equal(255, result[0]);
    }

    [Fact]
    public void ConvertFloat32ToU8_ClampsValuesBelowNegativeOne()
    {
        byte[] buffer = BitConverter.GetBytes(-3.0f);

        byte[] result = LtcDecoder.ConvertFloat32ToU8(buffer, buffer.Length, 1);

        Assert.Single(result);
        Assert.Equal(1, result[0]);
    }

    [Fact]
    public void ConvertFloat32ToU8_MultipleSamples()
    {
        byte[] buffer = new byte[12]; // 3 float samples
        BitConverter.GetBytes(0.0f).CopyTo(buffer, 0);
        BitConverter.GetBytes(0.5f).CopyTo(buffer, 4);
        BitConverter.GetBytes(-0.5f).CopyTo(buffer, 8);

        byte[] result = LtcDecoder.ConvertFloat32ToU8(buffer, buffer.Length, 1);

        Assert.Equal(3, result.Length);
        Assert.Equal(128, result[0]);             // 0.0 -> 128
        Assert.Equal((byte)(0.5f * 127 + 128), result[1]);  // ~191
        Assert.Equal((byte)(-0.5f * 127 + 128), result[2]); // ~64
    }

    [Fact]
    public void ConvertFloat32ToU8_PartialBytesRecorded()
    {
        // 8 bytes in buffer but only 4 bytesRecorded -> 1 sample
        byte[] buffer = new byte[8];
        BitConverter.GetBytes(0.5f).CopyTo(buffer, 0);
        BitConverter.GetBytes(-0.5f).CopyTo(buffer, 4);

        byte[] result = LtcDecoder.ConvertFloat32ToU8(buffer, 4, 1);

        Assert.Single(result);
    }

    // ---------------------------------------------------------------
    // ConvertPcm16ToU8 tests
    // ---------------------------------------------------------------

    [Fact]
    public void ConvertPcm16ToU8_SilenceConvertsTo128()
    {
        byte[] buffer = BitConverter.GetBytes((short)0);

        byte[] result = LtcDecoder.ConvertPcm16ToU8(buffer, buffer.Length, 1);

        Assert.Single(result);
        Assert.Equal(128, result[0]);
    }

    [Fact]
    public void ConvertPcm16ToU8_MaxPositiveConvertsTo255()
    {
        byte[] buffer = BitConverter.GetBytes(short.MaxValue);

        byte[] result = LtcDecoder.ConvertPcm16ToU8(buffer, buffer.Length, 1);

        Assert.Single(result);
        Assert.Equal(255, result[0]);
    }

    [Fact]
    public void ConvertPcm16ToU8_MaxNegativeConvertsTo0()
    {
        byte[] buffer = BitConverter.GetBytes(short.MinValue);

        byte[] result = LtcDecoder.ConvertPcm16ToU8(buffer, buffer.Length, 1);

        Assert.Single(result);
        Assert.Equal(0, result[0]);
    }

    [Fact]
    public void ConvertPcm16ToU8_MultipleSamples()
    {
        byte[] buffer = new byte[6]; // 3 PCM16 samples
        BitConverter.GetBytes((short)0).CopyTo(buffer, 0);
        BitConverter.GetBytes((short)16384).CopyTo(buffer, 2);
        BitConverter.GetBytes((short)-16384).CopyTo(buffer, 4);

        byte[] result = LtcDecoder.ConvertPcm16ToU8(buffer, buffer.Length, 1);

        Assert.Equal(3, result.Length);
        Assert.Equal(128, result[0]);  // 0 -> 128
        Assert.Equal(192, result[1]);  // 16384 -> (16384 + 32768) >> 8 = 192
        Assert.Equal(64, result[2]);   // -16384 -> (-16384 + 32768) >> 8 = 64
    }

    // ---------------------------------------------------------------
    // ConvertToLtcSamples dispatch tests
    // ---------------------------------------------------------------

    [Fact]
    public void ConvertToLtcSamples_32bit_DispatchesToFloat32()
    {
        byte[] buffer = BitConverter.GetBytes(0.0f);
        byte[] result = LtcDecoder.ConvertToLtcSamples(buffer, buffer.Length, 32, 1);

        Assert.Single(result);
        Assert.Equal(128, result[0]);
    }

    [Fact]
    public void ConvertToLtcSamples_16bit_DispatchesToPcm16()
    {
        byte[] buffer = BitConverter.GetBytes((short)0);
        byte[] result = LtcDecoder.ConvertToLtcSamples(buffer, buffer.Length, 16, 1);

        Assert.Single(result);
        Assert.Equal(128, result[0]);
    }

    [Fact]
    public void ConvertToLtcSamples_UnsupportedBitsPerSample_ReturnsEmpty()
    {
        byte[] buffer = new byte[8];
        byte[] result = LtcDecoder.ConvertToLtcSamples(buffer, buffer.Length, 24, 1);

        Assert.Empty(result);
    }

    // ---------------------------------------------------------------
    // DetermineFrameRate tests
    // ---------------------------------------------------------------

    [Fact]
    public void DetermineFrameRate_DropFrameTrue_Returns2997Drop()
    {
        var result = LtcDecoder.DetermineFrameRate(29, dropFrame: true);
        Assert.Equal(FrameRate.Fps2997Drop, result);
    }

    [Fact]
    public void DetermineFrameRate_DropFrameTrue_IgnoresMaxFrame()
    {
        // Even with a low frame number, drop frame flag takes precedence
        var result = LtcDecoder.DetermineFrameRate(10, dropFrame: true);
        Assert.Equal(FrameRate.Fps2997Drop, result);
    }

    [Fact]
    public void DetermineFrameRate_NoDropFrame_MaxFrame23_Returns24fps()
    {
        var result = LtcDecoder.DetermineFrameRate(23, dropFrame: false);
        Assert.Equal(FrameRate.Fps24, result);
    }

    [Fact]
    public void DetermineFrameRate_NoDropFrame_MaxFrame24_Returns25fps()
    {
        var result = LtcDecoder.DetermineFrameRate(24, dropFrame: false);
        Assert.Equal(FrameRate.Fps25, result);
    }

    [Fact]
    public void DetermineFrameRate_NoDropFrame_MaxFrame29_Returns30fps()
    {
        var result = LtcDecoder.DetermineFrameRate(29, dropFrame: false);
        Assert.Equal(FrameRate.Fps30, result);
    }

    [Fact]
    public void DetermineFrameRate_NoDropFrame_MaxFrame0_Returns24fps()
    {
        // Very low frame number defaults to 24fps
        var result = LtcDecoder.DetermineFrameRate(0, dropFrame: false);
        Assert.Equal(FrameRate.Fps24, result);
    }

    // ---------------------------------------------------------------
    // ToTimecodeValue tests
    // ---------------------------------------------------------------

    [Fact]
    public void ToTimecodeValue_CreatesCorrectValue()
    {
        var result = LtcDecoder.ToTimecodeValue(1, 2, 3, 4, FrameRate.Fps30);

        Assert.Equal(1, result.Hours);
        Assert.Equal(2, result.Minutes);
        Assert.Equal(3, result.Seconds);
        Assert.Equal(4, result.Frames);
        Assert.Equal(FrameRate.Fps30, result.FrameRate);
    }

    [Fact]
    public void ToTimecodeValue_DropFrame_CreatesCorrectValue()
    {
        var result = LtcDecoder.ToTimecodeValue(23, 59, 59, 29, FrameRate.Fps2997Drop);

        Assert.Equal(23, result.Hours);
        Assert.Equal(59, result.Minutes);
        Assert.Equal(59, result.Seconds);
        Assert.Equal(29, result.Frames);
        Assert.Equal(FrameRate.Fps2997Drop, result.FrameRate);
    }

    [Fact]
    public void ToTimecodeValue_ZeroTimecode()
    {
        var result = LtcDecoder.ToTimecodeValue(0, 0, 0, 0, FrameRate.Fps24);

        Assert.Equal(0, result.Hours);
        Assert.Equal(0, result.Minutes);
        Assert.Equal(0, result.Seconds);
        Assert.Equal(0, result.Frames);
        Assert.Equal(FrameRate.Fps24, result.FrameRate);
    }

    // ---------------------------------------------------------------
    // LtcFrameHelper.Extract tests
    // ---------------------------------------------------------------

    [Fact]
    public void LtcFrameHelper_Extract_AllZeros()
    {
        var (ptr, free) = MakeFramePtr();
        try
        {
            var (hours, minutes, seconds, frames, dropFrame) = LtcFrameHelper.Extract(ptr);
            Assert.Equal(0, hours);
            Assert.Equal(0, minutes);
            Assert.Equal(0, seconds);
            Assert.Equal(0, frames);
            Assert.False(dropFrame);
        }
        finally { free(); }
    }

    [Fact]
    public void LtcFrameHelper_Extract_01_02_03_04_NoDrop()
    {
        var (ptr, free) = MakeFramePtr(b0: 4, b2: 3, b4: 2, b6: 1);
        try
        {
            var (hours, minutes, seconds, frames, dropFrame) = LtcFrameHelper.Extract(ptr);
            Assert.Equal(1, hours);
            Assert.Equal(2, minutes);
            Assert.Equal(3, seconds);
            Assert.Equal(4, frames);
            Assert.False(dropFrame);
        }
        finally { free(); }
    }

    [Fact]
    public void LtcFrameHelper_Extract_23_59_59_29_NoDrop()
    {
        var (ptr, free) = MakeFramePtr(b0: 9, b1: 2, b2: 9, b3: 5, b4: 9, b5: 5, b6: 3, b7: 2);
        try
        {
            var (hours, minutes, seconds, frames, dropFrame) = LtcFrameHelper.Extract(ptr);
            Assert.Equal(23, hours);
            Assert.Equal(59, minutes);
            Assert.Equal(59, seconds);
            Assert.Equal(29, frames);
            Assert.False(dropFrame);
        }
        finally { free(); }
    }

    [Fact]
    public void LtcFrameHelper_Extract_DropFrameFlag()
    {
        var (ptr, free) = MakeFramePtr(b0: 2, b1: 0x04, b4: 1);
        try
        {
            var (hours, minutes, seconds, frames, dropFrame) = LtcFrameHelper.Extract(ptr);
            Assert.Equal(0, hours);
            Assert.Equal(1, minutes);
            Assert.Equal(0, seconds);
            Assert.Equal(2, frames);
            Assert.True(dropFrame);
        }
        finally { free(); }
    }

    [Fact]
    public void LtcFrameHelper_Extract_TensDigits()
    {
        var (ptr, free) = MakeFramePtr(b0: 7, b1: 1, b2: 6, b3: 5, b4: 4, b5: 3, b6: 2, b7: 1);
        try
        {
            var (hours, minutes, seconds, frames, dropFrame) = LtcFrameHelper.Extract(ptr);
            Assert.Equal(12, hours);
            Assert.Equal(34, minutes);
            Assert.Equal(56, seconds);
            Assert.Equal(17, frames);
            Assert.False(dropFrame);
        }
        finally { free(); }
    }

    [Fact]
    public void LtcFrameHelper_Extract_IgnoresHighBits()
    {
        var (ptr, free) = MakeFramePtr(b0: 0xF5, b1: 0xFC, b2: 0xF8, b3: 0xFB, b4: 0xF7, b5: 0xFC, b6: 0xF9, b7: 0xFE);
        try
        {
            var (hours, minutes, seconds, frames, dropFrame) = LtcFrameHelper.Extract(ptr);
            Assert.Equal(29, hours);
            Assert.Equal(47, minutes);
            Assert.Equal(38, seconds);
            Assert.Equal(5, frames);
            Assert.True(dropFrame);
        }
        finally { free(); }
    }

    // ---------------------------------------------------------------
    // LtcDecoder construction and Dispose tests
    // ---------------------------------------------------------------

    [Fact]
    public void LtcDecoder_CanBeConstructed()
    {
        using var decoder = new LtcDecoder();
        // Should not throw
    }

    [Fact]
    public void LtcDecoder_Dispose_IsIdempotent()
    {
        var decoder = new LtcDecoder();
        decoder.Dispose();
        decoder.Dispose(); // Should not throw
    }

    [Fact]
    public void LtcDecoder_ProcessSamples_WithoutInitialize_DoesNotThrow()
    {
        using var decoder = new LtcDecoder();
        byte[] buffer = new byte[4];

        // Without initialization, ProcessSamples safely returns without processing
        decoder.ProcessSamples(buffer, buffer.Length, 48000, 32, 1);
    }

    // ---------------------------------------------------------------
    // ILtcDecoder interface compliance
    // ---------------------------------------------------------------

    [Fact]
    public void LtcDecoder_ImplementsILtcDecoder()
    {
        using var decoder = new LtcDecoder();
        Assert.IsAssignableFrom<ILtcDecoder>(decoder);
    }

    [Fact]
    public void LtcDecoder_ImplementsIDisposable()
    {
        using var decoder = new LtcDecoder();
        Assert.IsAssignableFrom<IDisposable>(decoder);
    }

    [Fact]
    public void LtcDecoder_FrameDecodedEvent_CanSubscribe()
    {
        using var decoder = new LtcDecoder();
        bool eventFired = false;
        decoder.FrameDecoded += (_, _) => eventFired = true;
        // Event should be subscribable without error
        Assert.False(eventFired);
    }
}
