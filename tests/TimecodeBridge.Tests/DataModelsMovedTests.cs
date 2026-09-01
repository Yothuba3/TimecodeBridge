using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.Core.Models;
using Xunit;

namespace TimecodeBridge.Tests;

/// <summary>
/// Test-Driven Development: タスク 1.2 - データモデルの Core プロジェクトへの移動検証
/// </summary>
public class DataModelsMovedTests
{
    [Fact]
    public void TimecodeValue_ShouldBeInCoreModels()
    {
        // Arrange & Act
        var timecode = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30);

        // Assert
        Assert.NotNull(timecode);
        Assert.Equal("TimecodeBridge.Core.Models", typeof(TimecodeValue).Namespace);
    }

    [Fact]
    public void TimecodeOffset_ShouldBeInCoreModels()
    {
        // Arrange & Act
        var offset = new TimecodeOffset(false, 0, 0, 1, 0, FrameRate.Fps30);

        // Assert
        Assert.Equal("TimecodeBridge.Core.Models", typeof(TimecodeOffset).Namespace);
    }

    [Fact]
    public void FrameRate_ShouldBeInCoreModels()
    {
        // Arrange & Act
        var frameRate = FrameRate.Fps30;

        // Assert
        Assert.Equal("TimecodeBridge.Core.Models", typeof(FrameRate).Namespace);
    }

    [Fact]
    public void ProjectData_ShouldBeInCoreModels()
    {
        // Arrange & Act
        var projectData = new ProjectData();

        // Assert
        Assert.NotNull(projectData);
        Assert.Equal("TimecodeBridge.Core.Models", typeof(ProjectData).Namespace);
    }

    [Fact]
    public void Cue_ShouldBeInCoreModels()
    {
        // Arrange & Act
        var cue = new Cue
        {
            Id = "test",
            Name = "test",
            TriggerTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30),
            OscAddress = "/test",
        };

        // Assert
        Assert.NotNull(cue);
        Assert.Equal("TimecodeBridge.Core.Models", typeof(Cue).Namespace);
    }

    [Fact]
    public void OscHost_ShouldBeInCoreModels()
    {
        // Arrange & Act
        var host = new OscHost
        {
            Id = "test",
            Name = "test",
            IpAddress = "127.0.0.1",
            Port = 9000,
        };

        // Assert
        Assert.NotNull(host);
        Assert.Equal("TimecodeBridge.Core.Models", typeof(OscHost).Namespace);
    }

    [Fact]
    public void OscArgument_ShouldBeInCoreModels()
    {
        // Arrange & Act - OscArgument は抽象クラスなので派生クラスを使用
        var arg = new OscInt32Argument(42);

        // Assert
        Assert.NotNull(arg);
        Assert.Equal("TimecodeBridge.Core.Models", typeof(OscArgument).Namespace);
    }

    [Fact]
    public void AudioDeviceInfo_ShouldBeInCoreModels()
    {
        // Arrange & Act
        var deviceInfo = new AudioDeviceInfo("test-id", "Test Device", false);

        // Assert
        Assert.NotNull(deviceInfo);
        Assert.Equal("TimecodeBridge.Core.Models", typeof(AudioDeviceInfo).Namespace);
    }

    [Fact]
    public void TimecodeValue_ShouldMaintainFunctionality()
    {
        // Arrange
        var timecode = new TimecodeValue(1, 30, 15, 10, FrameRate.Fps30);

        // Act & Assert - TimecodeValue の主要機能が維持されていることを確認
        Assert.Equal(1, timecode.Hours);
        Assert.Equal(30, timecode.Minutes);
        Assert.Equal(15, timecode.Seconds);
        Assert.Equal(10, timecode.Frames);
        Assert.Equal(FrameRate.Fps30, timecode.FrameRate);
    }

    [Fact]
    public void TimecodeOffset_ShouldMaintainFunctionality()
    {
        // Arrange
        var offset = new TimecodeOffset(true, 0, 5, 30, 15, FrameRate.Fps30);

        // Act & Assert - TimecodeOffset の主要機能が維持されていることを確認
        Assert.True(offset.IsNegative);
        Assert.Equal(0, offset.Hours);
        Assert.Equal(5, offset.Minutes);
        Assert.Equal(30, offset.Seconds);
        Assert.Equal(15, offset.Frames);
    }

    [Fact]
    public void ProjectData_ShouldMaintainFunctionality()
    {
        // Arrange
        var projectData = new ProjectData
        {
            Cues = new List<Cue>(),
            Hosts = new List<OscHost>()
        };

        // Act & Assert - ProjectData の主要機能が維持されていることを確認
        Assert.NotNull(projectData.Cues);
        Assert.NotNull(projectData.Hosts);
        Assert.Empty(projectData.Cues);
        Assert.Empty(projectData.Hosts);
    }
}
