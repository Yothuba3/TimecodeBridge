using Xunit;
using TimecodeBridge.Core.Models;
using TimecodeBridge.macOS.Services.CoreAudio;

namespace TimecodeBridge.Tests.Services.CoreAudio;

/// <summary>
/// CoreAudioPlayback実装のテスト
/// </summary>
public class CoreAudioPlaybackTests
{
    [Fact]
    public void Constructor_ShouldInitializeSuccessfully()
    {
        // Act
        using var playback = new CoreAudioPlayback();

        // Assert
        Assert.NotNull(playback);
    }

    [Fact]
    public void Start_WithNullDevice_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var playback = new CoreAudioPlayback();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => playback.Start(null!));
    }

    [Fact]
    public void Stop_WithoutStart_ShouldNotThrow()
    {
        // Arrange
        using var playback = new CoreAudioPlayback();

        // Act & Assert
        var exception = Record.Exception(() => playback.Stop());
        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_ShouldCleanupResources()
    {
        // Arrange
        var playback = new CoreAudioPlayback();

        // Act
        playback.Dispose();

        // Assert - 2回目のDisposeも安全であること
        var exception = Record.Exception(() => playback.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void WriteSamples_WithNullBuffer_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var playback = new CoreAudioPlayback();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => playback.WriteSamples(null!, 0, 0));
    }

    [Fact]
    public void WriteSamples_WithInvalidOffset_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        using var playback = new CoreAudioPlayback();
        var samples = new byte[100];

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => playback.WriteSamples(samples, -1, 50));
        Assert.Throws<ArgumentOutOfRangeException>(() => playback.WriteSamples(samples, 101, 50));
    }

    [Fact]
    public void WriteSamples_WithInvalidCount_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        using var playback = new CoreAudioPlayback();
        var samples = new byte[100];

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => playback.WriteSamples(samples, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => playback.WriteSamples(samples, 0, 101));
        Assert.Throws<ArgumentOutOfRangeException>(() => playback.WriteSamples(samples, 50, 51));
    }

    [Fact]
    public void Start_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var playback = new CoreAudioPlayback();
        var device = new AudioDeviceInfo("test-id", "Test Device", false);
        playback.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => playback.Start(device));
    }

    [Fact]
    public void Stop_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var playback = new CoreAudioPlayback();
        playback.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => playback.Stop());
    }

    [Fact]
    public void WriteSamples_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var playback = new CoreAudioPlayback();
        var samples = new byte[100];
        playback.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => playback.WriteSamples(samples, 0, 100));
    }

    // 注意: 以下のテストはmacOS環境でCoreAudioデバイスが利用可能な場合のみ実行可能

    [Fact(Skip = "Requires macOS with CoreAudio hardware")]
    public void Start_WithValidDevice_ShouldInitializeAudioUnit()
    {
        // このテストはmacOS実機でのみ実行可能
    }

    [Fact(Skip = "Requires macOS with CoreAudio hardware")]
    public void WriteSamples_AfterStart_ShouldOutputAudio()
    {
        // このテストはmacOS実機でのみ実行可能
        // 実際のオーディオ出力が必要
    }
}
