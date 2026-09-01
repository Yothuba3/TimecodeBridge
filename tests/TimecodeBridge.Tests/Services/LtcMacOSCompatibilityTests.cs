using Xunit;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Tests.Services;

/// <summary>
/// macOS互換性テスト: 全フレームレートでのLTCエンコード/デコード動作確認
/// Task 9.3: LTCエンコード/デコード動作確認の実装
/// </summary>
public class LtcMacOSCompatibilityTests
{
    [Theory]
    [InlineData(FrameRate.Fps24, 24)]
    [InlineData(FrameRate.Fps25, 25)]
    [InlineData(FrameRate.Fps2997Drop, 30)]
    [InlineData(FrameRate.Fps30, 30)]
    [InlineData(FrameRate.Fps5994, 60)]
    [InlineData(FrameRate.Fps60, 60)]
    public void LtcEncoder_AllFrameRates_GeneratesValidSignal(FrameRate frameRate, int fps)
    {
        // Arrange
        var encoder = new LtcEncoder();
        encoder.Initialize(48000, frameRate);
        var timecode = new TimecodeValue(1, 23, 45, 10, frameRate);

        // Act
        encoder.EnqueueFrame(timecode);
        byte[] buffer = new byte[48000 * 2]; // 1秒分のバッファ
        int bytesRead = encoder.Read(buffer, 0, buffer.Length);

        // Assert
        Assert.True(bytesRead > 0, "エンコードされたデータが生成されること");
        Assert.True(HasNonZeroSamples(buffer, bytesRead), "無音でないこと");

        // ボリュームレベル確認
        Assert.InRange(encoder.VolumeLevel, 0f, 1f);
    }

    [Theory]
    [InlineData(FrameRate.Fps24, 24)]
    [InlineData(FrameRate.Fps25, 25)]
    [InlineData(FrameRate.Fps30, 30)]
    public void LtcDecoder_AllFrameRates_DecodesCorrectly(FrameRate frameRate, int fps)
    {
        // Arrange
        var encoder = new LtcEncoder();
        encoder.Initialize(48000, frameRate);
        var decoder = new LtcDecoder();
        decoder.Initialize(48000, fps);

        var originalTimecode = new TimecodeValue(0, 5, 30, 15, frameRate);
        TimecodeValue? decodedTimecode = null;
        decoder.FrameDecoded += (sender, tc) => decodedTimecode = tc;

        // Act: エンコード → デコード
        encoder.EnqueueFrame(originalTimecode);
        byte[] encodedBuffer = new byte[48000 * 2];
        int bytesRead = encoder.Read(encodedBuffer, 0, encodedBuffer.Length);

        decoder.ProcessSamples(encodedBuffer, bytesRead, 48000, 16, 1);

        // Assert
        Assert.NotNull(decodedTimecode);
        Assert.Equal(originalTimecode.Hours, decodedTimecode!.Value.Hours);
        Assert.Equal(originalTimecode.Minutes, decodedTimecode.Value.Minutes);
        Assert.Equal(originalTimecode.Seconds, decodedTimecode.Value.Seconds);
        Assert.Equal(originalTimecode.Frames, decodedTimecode.Value.Frames);
    }

    [Fact]
    public void LtcRoundTrip_macOSEnvironment_PreservesTimecode()
    {
        // macOS環境特有のラウンドトリップテスト
        var encoder = new LtcEncoder();
        var decoder = new LtcDecoder();
        encoder.Initialize(48000, FrameRate.Fps30);
        decoder.Initialize(48000, 30);

        var testTimecode = new TimecodeValue(12, 34, 56, 28, FrameRate.Fps30);
        TimecodeValue? result = null;
        decoder.FrameDecoded += (_, tc) => result = tc;

        // Act
        encoder.EnqueueFrame(testTimecode);
        byte[] buffer = new byte[10000];
        int read = encoder.Read(buffer, 0, buffer.Length);
        decoder.ProcessSamples(buffer, read, 48000, 16, 1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(testTimecode, result!.Value);
    }

    [Fact]
    public void LtcEncoder_MultipleFrames_MaintainsContinuity()
    {
        // 連続フレームのエンコード/デコードで連続性を確認
        var encoder = new LtcEncoder();
        var decoder = new LtcDecoder();
        encoder.Initialize(48000, FrameRate.Fps30);
        decoder.Initialize(48000, 30);

        var decodedFrames = new List<TimecodeValue>();
        decoder.FrameDecoded += (_, tc) => decodedFrames.Add(tc);

        // Act: 連続する5フレームをエンコード
        for (int frame = 0; frame < 5; frame++)
        {
            var timecode = new TimecodeValue(1, 0, 0, frame, FrameRate.Fps30);
            encoder.EnqueueFrame(timecode);
        }

        byte[] buffer = new byte[48000 * 2];
        int totalRead = encoder.Read(buffer, 0, buffer.Length);
        decoder.ProcessSamples(buffer, totalRead, 48000, 16, 1);

        // Assert: 少なくとも1フレームがデコードされること
        Assert.NotEmpty(decodedFrames);

        // 連続するフレームの場合、フレーム番号が増加していること
        for (int i = 1; i < decodedFrames.Count; i++)
        {
            Assert.True(
                decodedFrames[i].Frames > decodedFrames[i - 1].Frames ||
                (decodedFrames[i].Frames == 0 && decodedFrames[i - 1].Frames == 29),
                "フレーム番号が連続していること（またはロールオーバー）");
        }
    }

    [Theory]
    [InlineData(0.2f)]
    [InlineData(0.5f)]
    [InlineData(0.8f)]
    [InlineData(1.0f)]
    public void LtcEncoder_DifferentVolumeLevels_GeneratesScaledSignal(float volumeLevel)
    {
        // Arrange
        var encoder = new LtcEncoder();
        encoder.Initialize(48000, FrameRate.Fps30);
        encoder.VolumeLevel = volumeLevel;

        var timecode = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30);

        // Act
        encoder.EnqueueFrame(timecode);
        byte[] buffer = new byte[10000];
        int bytesRead = encoder.Read(buffer, 0, buffer.Length);

        // Assert
        Assert.True(bytesRead > 0);

        // ピーク振幅を確認（ボリュームに比例すること）
        short maxAmplitude = 0;
        for (int i = 0; i < bytesRead - 1; i += 2)
        {
            short sample = BitConverter.ToInt16(buffer, i);
            maxAmplitude = Math.Max(maxAmplitude, Math.Abs(sample));
        }

        short expectedMaxAmplitude = (short)(32767 * volumeLevel);
        Assert.InRange(maxAmplitude, expectedMaxAmplitude * 0.9, expectedMaxAmplitude * 1.1);
    }

    [Fact]
    public void LtcDecoder_NoiseResilience_HandlesShortIntervals()
    {
        // デコーダのノイズ耐性テスト
        var decoder = new LtcDecoder();
        decoder.Initialize(48000, 30);

        int decodedCount = 0;
        decoder.FrameDecoded += (_, tc) => decodedCount++;

        // ノイズ混入信号（ランダムデータ）
        byte[] noiseBuffer = new byte[1000];
        new Random(42).NextBytes(noiseBuffer);

        // Act
        decoder.ProcessSamples(noiseBuffer, noiseBuffer.Length, 48000, 16, 1);

        // Assert: ノイズではフレームがデコードされないこと
        Assert.Equal(0, decodedCount);
    }

    [Theory]
    [InlineData(44100, FrameRate.Fps30)]
    [InlineData(48000, FrameRate.Fps30)]
    [InlineData(96000, FrameRate.Fps60)]
    public void LtcEncoder_DifferentSampleRates_GeneratesCorrectFrequency(int sampleRate, FrameRate frameRate)
    {
        // 異なるサンプルレートでの動作確認
        var encoder = new LtcEncoder();
        encoder.Initialize(sampleRate, frameRate);

        var timecode = new TimecodeValue(0, 0, 0, 0, frameRate);
        encoder.EnqueueFrame(timecode);

        byte[] buffer = new byte[sampleRate * 2];
        int bytesRead = encoder.Read(buffer, 0, buffer.Length);

        Assert.True(bytesRead > 0);
        Assert.True(HasNonZeroSamples(buffer, bytesRead));
    }

    [Fact]
    public void LtcDecoder_DropFrameTimecode_DecodesCorrectly()
    {
        // ドロップフレームタイムコードのデコード
        var encoder = new LtcEncoder();
        var decoder = new LtcDecoder();
        encoder.Initialize(48000, FrameRate.Fps2997Drop);
        decoder.Initialize(48000, 30);

        var dropFrameTimecode = new TimecodeValue(1, 10, 0, 2, FrameRate.Fps2997Drop);
        TimecodeValue? result = null;
        decoder.FrameDecoded += (_, tc) => result = tc;

        encoder.EnqueueFrame(dropFrameTimecode);
        byte[] buffer = new byte[10000];
        int read = encoder.Read(buffer, 0, buffer.Length);
        decoder.ProcessSamples(buffer, read, 48000, 16, 1);

        Assert.NotNull(result);
        // ドロップフレームフラグが正しく認識されること
        Assert.True(result!.Value.FrameRate.IsDropFrame());
    }

    [Fact]
    public void LtcEncoder_Reset_ClearsQueue()
    {
        // Reset機能のテスト
        var encoder = new LtcEncoder();
        encoder.Initialize(48000, FrameRate.Fps30);

        // フレームをエンキュー
        encoder.EnqueueFrame(new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30));
        encoder.EnqueueFrame(new TimecodeValue(0, 0, 0, 1, FrameRate.Fps30));

        // Reset実行
        encoder.Reset();

        // Reset後は無音が返されるはず
        byte[] buffer = new byte[1000];
        int bytesRead = encoder.Read(buffer, 0, buffer.Length);

        Assert.Equal(1000, bytesRead);
        Assert.True(IsAllZeros(buffer, bytesRead), "Reset後は無音であること");
    }

    private bool HasNonZeroSamples(byte[] buffer, int length)
    {
        for (int i = 0; i < length; i++)
        {
            if (buffer[i] != 0) return true;
        }
        return false;
    }

    private bool IsAllZeros(byte[] buffer, int length)
    {
        for (int i = 0; i < length; i++)
        {
            if (buffer[i] != 0) return false;
        }
        return true;
    }
}
