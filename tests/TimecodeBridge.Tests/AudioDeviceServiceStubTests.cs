using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Tests;

/// <summary>
/// AudioDeviceService.macOS stub implementation tests
/// </summary>
public class AudioDeviceServiceStubTests
{
    private IAudioDeviceService CreateService()
    {
        // Will be replaced with actual stub instance
        return new TimecodeBridge.macOS.Services.AudioDeviceService();
    }

    [Fact]
    public void GetCaptureDevices_ReturnsNonEmptyList()
    {
        // Arrange
        var service = CreateService();

        // Act
        var devices = service.GetCaptureDevices();

        // Assert
        Assert.NotNull(devices);
        Assert.NotEmpty(devices);
    }

    [Fact]
    public void GetCaptureDevices_ReturnsDummyDevices()
    {
        // Arrange
        var service = CreateService();

        // Act
        var devices = service.GetCaptureDevices();

        // Assert
        Assert.All(devices, device =>
        {
            Assert.NotNull(device);
            Assert.NotEmpty(device.Id);
            Assert.NotEmpty(device.DisplayName);
            Assert.False(device.IsLoopback); // Capture devices should not be loopback
        });
    }

    [Fact]
    public void GetRenderDevices_ReturnsNonEmptyList()
    {
        // Arrange
        var service = CreateService();

        // Act
        var devices = service.GetRenderDevices();

        // Assert
        Assert.NotNull(devices);
        Assert.NotEmpty(devices);
    }

    [Fact]
    public void GetRenderDevices_ReturnsDummyDevices()
    {
        // Arrange
        var service = CreateService();

        // Act
        var devices = service.GetRenderDevices();

        // Assert
        Assert.All(devices, device =>
        {
            Assert.NotNull(device);
            Assert.NotEmpty(device.Id);
            Assert.NotEmpty(device.DisplayName);
        });
    }

    [Fact]
    public void GetCaptureDevices_ReturnsConsistentData()
    {
        // Arrange
        var service = CreateService();

        // Act
        var devices1 = service.GetCaptureDevices();
        var devices2 = service.GetCaptureDevices();

        // Assert
        Assert.Equal(devices1.Count, devices2.Count);
        for (int i = 0; i < devices1.Count; i++)
        {
            Assert.Equal(devices1[i].Id, devices2[i].Id);
            Assert.Equal(devices1[i].DisplayName, devices2[i].DisplayName);
            Assert.Equal(devices1[i].IsLoopback, devices2[i].IsLoopback);
        }
    }

    [Fact]
    public void GetRenderDevices_ReturnsConsistentData()
    {
        // Arrange
        var service = CreateService();

        // Act
        var devices1 = service.GetRenderDevices();
        var devices2 = service.GetRenderDevices();

        // Assert
        Assert.Equal(devices1.Count, devices2.Count);
        for (int i = 0; i < devices1.Count; i++)
        {
            Assert.Equal(devices1[i].Id, devices2[i].Id);
            Assert.Equal(devices1[i].DisplayName, devices2[i].DisplayName);
            Assert.Equal(devices1[i].IsLoopback, devices2[i].IsLoopback);
        }
    }

    [Fact]
    public void AudioDeviceInfo_ToString_ReturnsDisplayName()
    {
        // Arrange
        var service = CreateService();
        var devices = service.GetCaptureDevices();

        // Act & Assert
        Assert.All(devices, device =>
        {
            Assert.Equal(device.DisplayName, device.ToString());
        });
    }
}
