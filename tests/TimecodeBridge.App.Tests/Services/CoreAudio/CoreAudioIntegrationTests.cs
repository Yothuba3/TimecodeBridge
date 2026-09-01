using TimecodeBridge.Core.Services.Interfaces;
using Xunit;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.App.Services.CoreAudio;

namespace TimecodeBridge.Tests.Services.CoreAudio;

/// <summary>
/// CoreAudio統合テスト
/// </summary>
public class CoreAudioIntegrationTests
{
    [Fact]
    public void DeviceService_GetCaptureDevices_ShouldReturnValidList()
    {
        // Arrange
        var service = new CoreAudioDeviceService();

        // Act
        var devices = service.GetCaptureDevices();

        // Assert
        Assert.NotNull(devices);
        Assert.True(devices.Count > 0, "Should return at least one device (or error device)");

        foreach (var device in devices)
        {
            Assert.NotNull(device.Id);
            Assert.NotNull(device.DisplayName);
            Assert.False(string.IsNullOrWhiteSpace(device.DisplayName));
        }
    }

    [Fact]
    public void DeviceService_GetRenderDevices_ShouldReturnValidList()
    {
        // Arrange
        var service = new CoreAudioDeviceService();

        // Act
        var devices = service.GetRenderDevices();

        // Assert
        Assert.NotNull(devices);
        Assert.True(devices.Count > 0, "Should return at least one device (or error device)");

        foreach (var device in devices)
        {
            Assert.NotNull(device.Id);
            Assert.NotNull(device.DisplayName);
            Assert.False(string.IsNullOrWhiteSpace(device.DisplayName));
        }
    }

    [Fact]
    public void Capture_ShouldImplementIAudioCapture()
    {
        // Arrange & Act
        using var capture = new CoreAudioCapture();

        // Assert
        Assert.IsAssignableFrom<Core.Services.Interfaces.IAudioCapture>(capture);
    }

    [Fact]
    public void Playback_ShouldImplementIAudioPlayback()
    {
        // Arrange & Act
        using var playback = new CoreAudioPlayback();

        // Assert
        Assert.IsAssignableFrom<Core.Services.Interfaces.IAudioPlayback>(playback);
    }

    [Fact]
    public void Capture_LifecycleTest_ShouldNotThrow()
    {
        // Arrange
        using var capture = new CoreAudioCapture();
        bool samplesReceived = false;
        bool errorOccurred = false;

        capture.AudioSamplesAvailable += (sender, args) => samplesReceived = true;
        capture.ErrorOccurred += (sender, args) => errorOccurred = true;

        // Act & Assert
        // 開始前の停止は安全
        var exception = Record.Exception(() => capture.Stop());
        Assert.Null(exception);

        // Disposeは安全
        exception = Record.Exception(() => capture.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void Playback_LifecycleTest_ShouldNotThrow()
    {
        // Arrange
        using var playback = new CoreAudioPlayback();

        // Act & Assert
        // 開始前の停止は安全
        var exception = Record.Exception(() => playback.Stop());
        Assert.Null(exception);

        // 開始前のWriteSamplesは安全（バッファに追加される）
        byte[] samples = new byte[100];
        exception = Record.Exception(() => playback.WriteSamples(samples, 0, 100));
        Assert.Null(exception);

        // Disposeは安全
        exception = Record.Exception(() => playback.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void Capture_MultipleStartStop_ShouldNotThrow()
    {
        // Arrange
        using var capture = new CoreAudioCapture();

        // Act & Assert
        for (int i = 0; i < 3; i++)
        {
            var exception = Record.Exception(() => capture.Stop());
            Assert.Null(exception);
        }
    }

    [Fact]
    public void Playback_MultipleStartStop_ShouldNotThrow()
    {
        // Arrange
        using var playback = new CoreAudioPlayback();

        // Act & Assert
        for (int i = 0; i < 3; i++)
        {
            var exception = Record.Exception(() => playback.Stop());
            Assert.Null(exception);
        }
    }

    // ===========================================
    // 以下のテストはmacOS実機でのみ実行可能
    // ===========================================

    [Fact(Skip = "Requires macOS with CoreAudio hardware and TCC permissions")]
    public void Capture_Start_WithValidDevice_ShouldReceiveSamples()
    {
        // このテストはmacOS実機でのみ実行可能
        // TCC権限が必要
        //
        // 実装例:
        // var service = new CoreAudioDeviceService();
        // var devices = service.GetCaptureDevices();
        // var device = devices.First(d => !d.Id.Contains("error") && !d.Id.Contains("none"));
        //
        // using var capture = new CoreAudioCapture();
        // bool samplesReceived = false;
        // capture.AudioSamplesAvailable += (sender, args) =>
        // {
        //     samplesReceived = true;
        //     Assert.NotNull(args.Samples);
        //     Assert.True(args.Samples.Length > 0);
        // };
        //
        // capture.Start(device);
        // Thread.Sleep(1000); // 1秒待機
        // capture.Stop();
        //
        // Assert.True(samplesReceived, "Should have received audio samples");
    }

    [Fact(Skip = "Requires macOS with CoreAudio hardware")]
    public void Playback_Start_WithValidDevice_ShouldOutputSamples()
    {
        // このテストはmacOS実機でのみ実行可能
        //
        // 実装例:
        // var service = new CoreAudioDeviceService();
        // var devices = service.GetRenderDevices();
        // var device = devices.First(d => !d.Id.Contains("error") && !d.Id.Contains("none"));
        //
        // using var playback = new CoreAudioPlayback();
        // playback.Start(device);
        //
        // // 1kHz サイン波を生成
        // const int sampleRate = 48000;
        // const int duration = 1; // 1秒
        // byte[] samples = GenerateSineWave(1000, sampleRate, duration);
        //
        // playback.WriteSamples(samples, 0, samples.Length);
        // Thread.Sleep(duration * 1000 + 500); // 再生完了まで待機
        //
        // playback.Stop();
    }

    [Fact(Skip = "Requires macOS with CoreAudio hardware")]
    public void Capture_WithoutTCCPermission_ShouldThrowUnauthorizedAccessException()
    {
        // このテストは手動テストとして実施
        // TCC権限を拒否した状態でテストを実行
        //
        // 期待される結果:
        // - UnauthorizedAccessException がスローされる
        // - エラーメッセージに "TCC" が含まれる
    }

    [Fact(Skip = "Requires macOS with CoreAudio hardware")]
    public void Integration_CaptureToPlayback_30SecondsContinuous()
    {
        // Phase 2技術検証成果物基準
        // 48kHz モノラル、30秒連続キャプチャ成功
        //
        // 実装例:
        // var service = new CoreAudioDeviceService();
        // var captureDevice = service.GetCaptureDevices().First(d => !d.Id.Contains("error"));
        // var playbackDevice = service.GetRenderDevices().First(d => !d.Id.Contains("error"));
        //
        // using var capture = new CoreAudioCapture();
        // using var playback = new CoreAudioPlayback();
        //
        // int samplesReceived = 0;
        // capture.AudioSamplesAvailable += (sender, args) =>
        // {
        //     samplesReceived++;
        //     // Float -> Int16 -> Byte変換してプレイバックに送信
        //     byte[] bytes = ConvertFloatToInt16Bytes(args.Samples);
        //     playback.WriteSamples(bytes, 0, bytes.Length);
        // };
        //
        // capture.Start(captureDevice);
        // playback.Start(playbackDevice);
        //
        // Thread.Sleep(30000); // 30秒
        //
        // capture.Stop();
        // playback.Stop();
        //
        // Assert.True(samplesReceived > 0, "Should have received samples during 30 seconds");
    }
}
