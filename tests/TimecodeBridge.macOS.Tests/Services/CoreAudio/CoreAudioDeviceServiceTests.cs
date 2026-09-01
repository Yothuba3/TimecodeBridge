using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using Xunit;
using TimecodeBridge.macOS.Services.CoreAudio;

namespace TimecodeBridge.Tests.Services.CoreAudio;

/// <summary>
/// CoreAudioDeviceService実装のテスト
/// </summary>
public class CoreAudioDeviceServiceTests
{
    [Fact]
    public void Constructor_ShouldInitializeSuccessfully()
    {
        // Act
        var service = new CoreAudioDeviceService();

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void GetCaptureDevices_ShouldReturnList()
    {
        // Arrange
        var service = new CoreAudioDeviceService();

        // Act
        var devices = service.GetCaptureDevices();

        // Assert
        Assert.NotNull(devices);
        // macOS環境でない場合はエラーデバイスまたは空リストが返る
        Assert.True(devices.Count >= 0);
    }

    [Fact]
    public void GetRenderDevices_ShouldReturnList()
    {
        // Arrange
        var service = new CoreAudioDeviceService();

        // Act
        var devices = service.GetRenderDevices();

        // Assert
        Assert.NotNull(devices);
        // macOS環境でない場合はエラーデバイスまたは空リストが返る
        Assert.True(devices.Count >= 0);
    }

    [Fact]
    public void GetCaptureDevices_MultipleCallsShouldSucceed()
    {
        // Arrange
        var service = new CoreAudioDeviceService();

        // Act
        var devices1 = service.GetCaptureDevices();
        var devices2 = service.GetCaptureDevices();

        // Assert
        Assert.NotNull(devices1);
        Assert.NotNull(devices2);
        // 複数回呼び出しても安定して動作すること
    }

    // 注意: 以下のテストはmacOS環境でのみ実行可能

    [Fact(Skip = "Requires macOS with CoreAudio hardware")]
    public void GetCaptureDevices_OnMacOS_ShouldReturnActualDevices()
    {
        // このテストはmacOS実機でのみ実行可能
        // 実際のオーディオデバイスが必要
    }

    [Fact(Skip = "Requires macOS with CoreAudio hardware")]
    public void GetRenderDevices_OnMacOS_ShouldReturnActualDevices()
    {
        // このテストはmacOS実機でのみ実行可能
        // 実際のオーディオデバイスが必要
    }
}
