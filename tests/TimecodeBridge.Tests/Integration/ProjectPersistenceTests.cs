using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
namespace TimecodeBridge.Tests.Integration;

using System.IO;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Services;

public class ProjectPersistenceTests : IDisposable
{
    private readonly string _tempDir;

    public ProjectPersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TimecodeBridge_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void ProjectPersistence_SaveAndLoad_RoundTrip()
    {
        // Arrange
        var service = new ProjectService();
        var projectPath = Path.Combine(_tempDir, "test_project.json");

        var data = new ProjectData
        {
            Cues =
            [
                new Cue
                {
                    Id = "cue-1",
                    Name = "Intro Cue",
                    Memo = "Start of show",
                    TriggerTime = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30),
                    OscAddress = "/cue/intro",
                    Arguments = [new OscInt32Argument(1)],
                    TargetHostIds = ["host1", "host2"],
                    IsEnabled = true,
                },
                new Cue
                {
                    Id = "cue-2",
                    Name = "Outro Cue",
                    Memo = "End of show",
                    TriggerTime = new TimecodeValue(1, 30, 0, 0, FrameRate.Fps30),
                    OscAddress = "/cue/outro",
                    Arguments = [],
                    TargetHostIds = ["host1"],
                    IsEnabled = false,
                },
            ],
            Hosts =
            [
                new OscHost
                {
                    Id = "host1",
                    Name = "Main Console",
                    IpAddress = "192.168.1.10",
                    Port = 9000,
                    IsEnabled = true,
                },
                new OscHost
                {
                    Id = "host2",
                    Name = "Backup Console",
                    IpAddress = "192.168.1.20",
                    Port = 9001,
                    IsEnabled = false,
                },
            ],
            RelaySettings = new RelaySettings
            {
                OscAddressPattern = "/tc/relay",
                ContinuousInterval = new RelayInterval(RelayIntervalMode.Custom, 100),
                TargetHostIds = ["host1"],
                IsContinuousEnabled = true,
            },
            Offset = new TimecodeOffset(true, 0, 0, 5, 10, FrameRate.Fps30),
            SourceSettings = new TimecodeSourceSettings
            {
                SourceType = TimecodeSourceType.Ltc,
                DeviceId = "audio-device-123",
            },
        };

        // Act: save then load
        service.SaveProject(projectPath, data);
        var loaded = service.LoadProject(projectPath);

        // Assert: Cues
        Assert.Equal(2, loaded.Cues.Count);

        Assert.Equal("cue-1", loaded.Cues[0].Id);
        Assert.Equal("Intro Cue", loaded.Cues[0].Name);
        Assert.Equal("Start of show", loaded.Cues[0].Memo);
        Assert.Equal(new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30), loaded.Cues[0].TriggerTime);
        Assert.Equal("/cue/intro", loaded.Cues[0].OscAddress);
        Assert.Single(loaded.Cues[0].Arguments);
        Assert.Equal(2, loaded.Cues[0].TargetHostIds.Count);
        Assert.True(loaded.Cues[0].IsEnabled);

        Assert.Equal("cue-2", loaded.Cues[1].Id);
        Assert.False(loaded.Cues[1].IsEnabled);

        // Assert: Hosts
        Assert.Equal(2, loaded.Hosts.Count);
        Assert.Equal("host1", loaded.Hosts[0].Id);
        Assert.Equal("Main Console", loaded.Hosts[0].Name);
        Assert.Equal("192.168.1.10", loaded.Hosts[0].IpAddress);
        Assert.Equal(9000, loaded.Hosts[0].Port);
        Assert.True(loaded.Hosts[0].IsEnabled);

        Assert.Equal("host2", loaded.Hosts[1].Id);
        Assert.False(loaded.Hosts[1].IsEnabled);

        // Assert: RelaySettings
        Assert.Equal("/tc/relay", loaded.RelaySettings.OscAddressPattern);
        Assert.Equal(RelayIntervalMode.Custom, loaded.RelaySettings.ContinuousInterval.Mode);
        Assert.Equal(100, loaded.RelaySettings.ContinuousInterval.IntervalMs);
        Assert.Single(loaded.RelaySettings.TargetHostIds);
        Assert.Equal("host1", loaded.RelaySettings.TargetHostIds[0]);
        Assert.True(loaded.RelaySettings.IsContinuousEnabled);

        // Assert: Offset
        Assert.True(loaded.Offset.IsNegative);
        Assert.Equal(0, loaded.Offset.Hours);
        Assert.Equal(0, loaded.Offset.Minutes);
        Assert.Equal(5, loaded.Offset.Seconds);
        Assert.Equal(10, loaded.Offset.Frames);

        // Assert: SourceSettings
        Assert.Equal(TimecodeSourceType.Ltc, loaded.SourceSettings.SourceType);
        Assert.Equal("audio-device-123", loaded.SourceSettings.DeviceId);
    }

    [Fact]
    public void ProjectPersistence_CuesWithArguments_PreservedOnRoundTrip()
    {
        // Arrange
        var service = new ProjectService();
        var projectPath = Path.Combine(_tempDir, "args_project.json");

        var data = new ProjectData
        {
            Cues =
            [
                new Cue
                {
                    Id = "cue-args",
                    Name = "Cue With Arguments",
                    TriggerTime = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30),
                    OscAddress = "/cue/args",
                    Arguments =
                    [
                        new OscInt32Argument(42),
                        new OscFloat32Argument(3.14f),
                        new OscStringArgument("hello"),
                    ],
                    TargetHostIds = ["host1"],
                },
            ],
        };

        // Act
        service.SaveProject(projectPath, data);
        var loaded = service.LoadProject(projectPath);

        // Assert
        Assert.Single(loaded.Cues);
        var args = loaded.Cues[0].Arguments;
        Assert.Equal(3, args.Count);

        var intArg = Assert.IsType<OscInt32Argument>(args[0]);
        Assert.Equal(42, intArg.Value);

        var floatArg = Assert.IsType<OscFloat32Argument>(args[1]);
        Assert.Equal(3.14f, floatArg.Value);

        var strArg = Assert.IsType<OscStringArgument>(args[2]);
        Assert.Equal("hello", strArg.Value);
    }

    [Fact]
    public void ProjectPersistence_RelaySettings_PreservedOnRoundTrip()
    {
        // Arrange
        var service = new ProjectService();
        var projectPath = Path.Combine(_tempDir, "relay_project.json");

        var data = new ProjectData
        {
            RelaySettings = new RelaySettings
            {
                OscAddressPattern = "/my/timecode",
                ContinuousInterval = new RelayInterval(RelayIntervalMode.EveryFrame, 0),
                TargetHostIds = ["hostA", "hostB"],
                IsContinuousEnabled = false,
            },
        };

        // Act
        service.SaveProject(projectPath, data);
        var loaded = service.LoadProject(projectPath);

        // Assert
        Assert.Equal("/my/timecode", loaded.RelaySettings.OscAddressPattern);
        Assert.Equal(RelayIntervalMode.EveryFrame, loaded.RelaySettings.ContinuousInterval.Mode);
        Assert.Equal(0, loaded.RelaySettings.ContinuousInterval.IntervalMs);
        Assert.Equal(2, loaded.RelaySettings.TargetHostIds.Count);
        Assert.Equal("hostA", loaded.RelaySettings.TargetHostIds[0]);
        Assert.Equal("hostB", loaded.RelaySettings.TargetHostIds[1]);
        Assert.False(loaded.RelaySettings.IsContinuousEnabled);

        // Verify with different settings
        var data2 = new ProjectData
        {
            RelaySettings = new RelaySettings
            {
                OscAddressPattern = "/tc",
                ContinuousInterval = new RelayInterval(RelayIntervalMode.Custom, 250),
                TargetHostIds = ["hostC"],
                IsContinuousEnabled = true,
            },
        };

        var projectPath2 = Path.Combine(_tempDir, "relay_project2.json");
        service.SaveProject(projectPath2, data2);
        var loaded2 = service.LoadProject(projectPath2);

        Assert.Equal("/tc", loaded2.RelaySettings.OscAddressPattern);
        Assert.Equal(RelayIntervalMode.Custom, loaded2.RelaySettings.ContinuousInterval.Mode);
        Assert.Equal(250, loaded2.RelaySettings.ContinuousInterval.IntervalMs);
        Assert.True(loaded2.RelaySettings.IsContinuousEnabled);
    }
}
