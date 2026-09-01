using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Tests;

/// <summary>
/// Test class to verify that core service interfaces have been successfully moved to TimecodeBridge.Core.
/// These tests validate Task 1.3 completion.
/// </summary>
public class CoreInterfacesMovedTests
{
    [Fact]
    public void ITimecodeEngine_ExistsInCoreProject()
    {
        // Arrange & Act
        var interfaceType = typeof(ITimecodeEngine);

        // Assert
        Assert.NotNull(interfaceType);
        Assert.True(interfaceType.IsInterface);
        Assert.Equal("TimecodeBridge.Core.Services.Interfaces", interfaceType.Namespace);
    }

    [Fact]
    public void ILtcEncoder_ExistsInCoreProject()
    {
        // Arrange & Act
        var interfaceType = typeof(ILtcEncoder);

        // Assert
        Assert.NotNull(interfaceType);
        Assert.True(interfaceType.IsInterface);
        Assert.Equal("TimecodeBridge.Core.Services.Interfaces", interfaceType.Namespace);
    }

    [Fact]
    public void ILtcDecoder_ExistsInCoreProject()
    {
        // Arrange & Act
        var interfaceType = typeof(ILtcDecoder);

        // Assert
        Assert.NotNull(interfaceType);
        Assert.True(interfaceType.IsInterface);
        Assert.Equal("TimecodeBridge.Core.Services.Interfaces", interfaceType.Namespace);
    }

    [Fact]
    public void ICueManager_ExistsInCoreProject()
    {
        // Arrange & Act
        var interfaceType = typeof(ICueManager);

        // Assert
        Assert.NotNull(interfaceType);
        Assert.True(interfaceType.IsInterface);
        Assert.Equal("TimecodeBridge.Core.Services.Interfaces", interfaceType.Namespace);
    }

    [Fact]
    public void IOscSender_ExistsInCoreProject()
    {
        // Arrange & Act
        var interfaceType = typeof(IOscSender);

        // Assert
        Assert.NotNull(interfaceType);
        Assert.True(interfaceType.IsInterface);
        Assert.Equal("TimecodeBridge.Core.Services.Interfaces", interfaceType.Namespace);
    }

    [Fact]
    public void IProjectService_ExistsInCoreProject()
    {
        // Arrange & Act
        var interfaceType = typeof(IProjectService);

        // Assert
        Assert.NotNull(interfaceType);
        Assert.True(interfaceType.IsInterface);
        Assert.Equal("TimecodeBridge.Core.Services.Interfaces", interfaceType.Namespace);
    }

    [Fact]
    public void IFileDialogService_ExistsInCoreProject()
    {
        // Arrange & Act
        var interfaceType = typeof(IFileDialogService);

        // Assert
        Assert.NotNull(interfaceType);
        Assert.True(interfaceType.IsInterface);
        Assert.Equal("TimecodeBridge.Core.Services.Interfaces", interfaceType.Namespace);
    }

    [Fact]
    public void IAudioDeviceService_ExistsInCoreProject()
    {
        // Arrange & Act
        var interfaceType = typeof(IAudioDeviceService);

        // Assert
        Assert.NotNull(interfaceType);
        Assert.True(interfaceType.IsInterface);
        Assert.Equal("TimecodeBridge.Core.Services.Interfaces", interfaceType.Namespace);
    }

    [Fact]
    public void ITimecodeGenerator_ExistsInCoreProject()
    {
        // Arrange & Act
        var interfaceType = typeof(ITimecodeGenerator);

        // Assert
        Assert.NotNull(interfaceType);
        Assert.True(interfaceType.IsInterface);
        Assert.Equal("TimecodeBridge.Core.Services.Interfaces", interfaceType.Namespace);
    }

    [Fact]
    public void ITimecodeRelay_ExistsInCoreProject()
    {
        // Arrange & Act
        var interfaceType = typeof(ITimecodeRelay);

        // Assert
        Assert.NotNull(interfaceType);
        Assert.True(interfaceType.IsInterface);
        Assert.Equal("TimecodeBridge.Core.Services.Interfaces", interfaceType.Namespace);
    }

    [Fact]
    public void IOscTransport_ExistsInCoreProject()
    {
        // Arrange & Act
        var interfaceType = typeof(IOscTransport);

        // Assert
        Assert.NotNull(interfaceType);
        Assert.True(interfaceType.IsInterface);
        Assert.Equal("TimecodeBridge.Core.Services.Interfaces", interfaceType.Namespace);
    }

    [Fact]
    public void IHostRegistry_ExistsInCoreProject()
    {
        // Arrange & Act
        var interfaceType = typeof(IHostRegistry);

        // Assert
        Assert.NotNull(interfaceType);
        Assert.True(interfaceType.IsInterface);
        Assert.Equal("TimecodeBridge.Core.Services.Interfaces", interfaceType.Namespace);
    }

    [Fact]
    public void IAudioCapture_ExistsInCoreProject()
    {
        // Arrange & Act
        var interfaceType = typeof(IAudioCapture);

        // Assert
        Assert.NotNull(interfaceType);
        Assert.True(interfaceType.IsInterface);
        Assert.Equal("TimecodeBridge.Core.Services.Interfaces", interfaceType.Namespace);
    }

    [Fact]
    public void IAudioPlayback_ExistsInCoreProject()
    {
        // Arrange & Act
        var interfaceType = typeof(IAudioPlayback);

        // Assert
        Assert.NotNull(interfaceType);
        Assert.True(interfaceType.IsInterface);
        Assert.Equal("TimecodeBridge.Core.Services.Interfaces", interfaceType.Namespace);
    }

    [Fact]
    public void EventArgs_Classes_ExistInCoreProject()
    {
        // Verify event args are also in Core
        Assert.Equal("TimecodeBridge.Core.Services", typeof(TimecodeUpdatedEventArgs).Namespace);
        Assert.Equal("TimecodeBridge.Core.Services", typeof(TimecodeStatusChangedEventArgs).Namespace);
        Assert.Equal("TimecodeBridge.Core.Services", typeof(AudioSamplesEventArgs).Namespace);
        Assert.Equal("TimecodeBridge.Core.Services", typeof(AudioErrorEventArgs).Namespace);
        Assert.Equal("TimecodeBridge.Core.Services.Interfaces", typeof(OscSendResultEventArgs).Namespace);
        Assert.Equal("TimecodeBridge.Core.Services.Interfaces", typeof(CueTriggeredEventArgs).Namespace);
        Assert.Equal("TimecodeBridge.Core.Services.Interfaces", typeof(HostChangedEventArgs).Namespace);
    }
}
