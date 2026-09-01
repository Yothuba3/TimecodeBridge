using TimecodeBridge.Core.Services.Interfaces;
using Xunit;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.App.Services.CoreAudio;

namespace TimecodeBridge.Tests.Services.CoreAudio;

/// <summary>
/// CoreAudioCapture実装のテスト
/// </summary>
public class CoreAudioCaptureTests
{
    [Fact]
    public void Constructor_ShouldInitializeSuccessfully()
    {
        // Act
        using var capture = new CoreAudioCapture();

        // Assert
        Assert.NotNull(capture);
    }

    [Fact]
    public void Start_WithNullDevice_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var capture = new CoreAudioCapture();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => capture.Start(null!));
    }

    [Fact]
    public void Stop_WithoutStart_ShouldNotThrow()
    {
        // Arrange
        using var capture = new CoreAudioCapture();

        // Act & Assert
        var exception = Record.Exception(() => capture.Stop());
        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_ShouldCleanupResources()
    {
        // Arrange
        var capture = new CoreAudioCapture();

        // Act
        capture.Dispose();

        // Assert - 2回目のDisposeも安全であること
        var exception = Record.Exception(() => capture.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void AudioSamplesAvailable_ShouldBeSubscribable()
    {
        // Arrange
        using var capture = new CoreAudioCapture();
        bool eventFired = false;
        float[]? receivedSamples = null;

        capture.AudioSamplesAvailable += (sender, args) =>
        {
            eventFired = true;
            receivedSamples = args.Samples;
        };

        // Act - イベントハンドラが正しく登録されたことを確認
        Assert.NotNull(capture);

        // Assert
        Assert.False(eventFired); // まだイベントは発火していない
        Assert.Null(receivedSamples);
    }

    [Fact]
    public void ErrorOccurred_ShouldBeSubscribable()
    {
        // Arrange
        using var capture = new CoreAudioCapture();
        bool eventFired = false;
        string? errorMessage = null;

        capture.ErrorOccurred += (sender, args) =>
        {
            eventFired = true;
            errorMessage = args.Message;
        };

        // Act - イベントハンドラが正しく登録されたことを確認
        Assert.NotNull(capture);

        // Assert
        Assert.False(eventFired); // まだイベントは発火していない
        Assert.Null(errorMessage);
    }

    [Fact]
    public void Start_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var capture = new CoreAudioCapture();
        var device = new AudioDeviceInfo("test-id", "Test Device", false);
        capture.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => capture.Start(device));
    }

    [Fact]
    public void Stop_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var capture = new CoreAudioCapture();
        capture.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => capture.Stop());
    }

    // 注意: 以下のテストはmacOS環境でCoreAudioデバイスが利用可能な場合のみ実行可能
    // CI環境では手動テストマークとして記述

    [Fact(Skip = "Requires macOS with CoreAudio hardware and TCC permissions")]
    public void Start_WithValidDevice_ShouldInitializeAudioUnit()
    {
        // このテストはmacOS実機でのみ実行可能
        // TCC権限が必要
    }

    [Fact(Skip = "Requires macOS with CoreAudio hardware and TCC permissions")]
    public void Start_ShouldFireAudioSamplesAvailableEvent()
    {
        // このテストはmacOS実機でのみ実行可能
        // 実際のオーディオキャプチャが必要
    }

    [Fact(Skip = "Requires macOS with CoreAudio hardware")]
    public void Start_WithInvalidDeviceId_ShouldFireErrorOccurredEvent()
    {
        // このテストはmacOS実機でのみ実行可能
    }
}
