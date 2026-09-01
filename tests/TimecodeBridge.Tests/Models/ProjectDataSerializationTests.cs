using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
namespace TimecodeBridge.Tests.Models;

using System.Text.Json;
using TimecodeBridge.Core.Models;

public class ProjectDataSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = ProjectData.CreateJsonOptions();

    [Fact]
    public void ProjectData_RoundTrip_PreservesAllFields()
    {
        var original = CreateSampleProjectData();

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<ProjectData>(json, JsonOptions)!;

        Assert.Equal(original.Cues.Count, deserialized.Cues.Count);
        Assert.Equal(original.Hosts.Count, deserialized.Hosts.Count);
        Assert.Equal(original.Offset.Hours, deserialized.Offset.Hours);
        Assert.Equal(original.RelaySettings.OscAddressPattern, deserialized.RelaySettings.OscAddressPattern);
    }

    [Fact]
    public void Cue_Serialization_PreservesTriggerTime()
    {
        var original = CreateSampleProjectData();

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<ProjectData>(json, JsonOptions)!;

        var originalCue = original.Cues[0];
        var deserializedCue = deserialized.Cues[0];

        Assert.Equal(originalCue.TriggerTime, deserializedCue.TriggerTime);
        Assert.Equal(originalCue.Name, deserializedCue.Name);
        Assert.Equal(originalCue.OscAddress, deserializedCue.OscAddress);
        Assert.Equal(originalCue.IsEnabled, deserializedCue.IsEnabled);
    }

    [Fact]
    public void OscArgument_Int32_RoundTrip()
    {
        var original = new OscInt32Argument(42);

        var json = JsonSerializer.Serialize<OscArgument>(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<OscArgument>(json, JsonOptions)!;

        var intArg = Assert.IsType<OscInt32Argument>(deserialized);
        Assert.Equal(42, intArg.Value);
    }

    [Fact]
    public void OscArgument_Float32_RoundTrip()
    {
        var original = new OscFloat32Argument(3.14f);

        var json = JsonSerializer.Serialize<OscArgument>(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<OscArgument>(json, JsonOptions)!;

        var floatArg = Assert.IsType<OscFloat32Argument>(deserialized);
        Assert.Equal(3.14f, floatArg.Value);
    }

    [Fact]
    public void OscArgument_String_RoundTrip()
    {
        var original = new OscStringArgument("hello world");

        var json = JsonSerializer.Serialize<OscArgument>(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<OscArgument>(json, JsonOptions)!;

        var strArg = Assert.IsType<OscStringArgument>(deserialized);
        Assert.Equal("hello world", strArg.Value);
    }

    [Fact]
    public void CueArguments_MixedTypes_RoundTrip()
    {
        var original = CreateSampleProjectData();

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<ProjectData>(json, JsonOptions)!;

        var args = deserialized.Cues[0].Arguments;
        Assert.Equal(3, args.Count);
        Assert.IsType<OscInt32Argument>(args[0]);
        Assert.IsType<OscFloat32Argument>(args[1]);
        Assert.IsType<OscStringArgument>(args[2]);
    }

    [Fact]
    public void Host_RoundTrip_PreservesAllFields()
    {
        var original = CreateSampleProjectData();

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<ProjectData>(json, JsonOptions)!;

        var host = deserialized.Hosts[0];
        Assert.Equal("host-1", host.Id);
        Assert.Equal("Main", host.Name);
        Assert.Equal("192.168.1.100", host.IpAddress);
        Assert.Equal(8000, host.Port);
        Assert.True(host.IsEnabled);
    }

    [Fact]
    public void RelaySettings_RoundTrip()
    {
        var original = CreateSampleProjectData();

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<ProjectData>(json, JsonOptions)!;

        Assert.Equal("/timecode", deserialized.RelaySettings.OscAddressPattern);
        Assert.Equal(RelayIntervalMode.EveryFrame, deserialized.RelaySettings.ContinuousInterval.Mode);
        Assert.True(deserialized.RelaySettings.IsContinuousEnabled);
        Assert.Single(deserialized.RelaySettings.TargetHostIds);
    }

    [Fact]
    public void RelaySettings_CustomInterval_RoundTrip()
    {
        var data = CreateSampleProjectData();
        data.RelaySettings.ContinuousInterval = new RelayInterval(RelayIntervalMode.Custom, 100);

        var json = JsonSerializer.Serialize(data, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<ProjectData>(json, JsonOptions)!;

        Assert.Equal(RelayIntervalMode.Custom, deserialized.RelaySettings.ContinuousInterval.Mode);
        Assert.Equal(100, deserialized.RelaySettings.ContinuousInterval.IntervalMs);
    }

    [Fact]
    public void SourceSettings_RoundTrip()
    {
        var original = CreateSampleProjectData();

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<ProjectData>(json, JsonOptions)!;

        Assert.Equal(TimecodeSourceType.Ltc, deserialized.SourceSettings.SourceType);
        Assert.Equal("device-123", deserialized.SourceSettings.DeviceId);
    }

    [Fact]
    public void EmptyProjectData_Serializes()
    {
        var empty = new ProjectData();

        var json = JsonSerializer.Serialize(empty, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<ProjectData>(json, JsonOptions)!;

        Assert.Empty(deserialized.Cues);
        Assert.Empty(deserialized.Hosts);
    }

    [Fact]
    public void OscTriggerPanel_RoundTrip_PreservesGridAndButtons()
    {
        var data = new ProjectData
        {
            OscTriggerPanel = new OscTriggerPanelSettings
            {
                Rows = 3,
                Columns = 5,
                Buttons =
                [
                    new OscTriggerButton
                    {
                        Id = "btn-1",
                        Row = 1,
                        Column = 2,
                        Label = "GO",
                        OscAddress = "/pon/1",
                        Arguments =
                        [
                            new OscInt32Argument(1),
                            new OscFloat32Argument(0.25f),
                            new OscStringArgument("x"),
                        ],
                        TargetHostIds = ["host-1", "host-2"],
                    },
                ],
            },
        };

        var json = JsonSerializer.Serialize(data, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<ProjectData>(json, JsonOptions)!;

        Assert.Equal(3, deserialized.OscTriggerPanel.Rows);
        Assert.Equal(5, deserialized.OscTriggerPanel.Columns);

        var btn = Assert.Single(deserialized.OscTriggerPanel.Buttons);
        Assert.Equal("btn-1", btn.Id);
        Assert.Equal(1, btn.Row);
        Assert.Equal(2, btn.Column);
        Assert.Equal("GO", btn.Label);
        Assert.Equal("/pon/1", btn.OscAddress);
        Assert.Equal(3, btn.Arguments.Count);
        Assert.IsType<OscInt32Argument>(btn.Arguments[0]);
        Assert.IsType<OscFloat32Argument>(btn.Arguments[1]);
        Assert.IsType<OscStringArgument>(btn.Arguments[2]);
        Assert.Equal(2, btn.TargetHostIds.Count);
    }

    [Fact]
    public void OscTriggerPanel_MissingSection_UsesDefaults()
    {
        // 旧バージョンのプロジェクトファイル（oscTriggerPanel セクションなし）
        var json = "{\"cues\":[],\"hosts\":[]}";

        var deserialized = JsonSerializer.Deserialize<ProjectData>(json, JsonOptions)!;

        Assert.NotNull(deserialized.OscTriggerPanel);
        Assert.Equal(4, deserialized.OscTriggerPanel.Rows);
        Assert.Equal(4, deserialized.OscTriggerPanel.Columns);
        Assert.Empty(deserialized.OscTriggerPanel.Buttons);
    }

    private static ProjectData CreateSampleProjectData()
    {
        return new ProjectData
        {
            Cues =
            [
                new Cue
                {
                    Id = "cue-1",
                    Name = "Scene1",
                    Memo = "Opening",
                    TriggerTime = new TimecodeValue(0, 1, 0, 0, FrameRate.Fps24),
                    OscAddress = "/scene/1",
                    Arguments =
                    [
                        new OscInt32Argument(1),
                        new OscFloat32Argument(0.5f),
                        new OscStringArgument("go"),
                    ],
                    TargetHostIds = ["host-1"],
                    IsEnabled = true,
                },
            ],
            Hosts =
            [
                new OscHost
                {
                    Id = "host-1",
                    Name = "Main",
                    IpAddress = "192.168.1.100",
                    Port = 8000,
                    IsEnabled = true,
                },
            ],
            RelaySettings = new RelaySettings
            {
                OscAddressPattern = "/timecode",
                ContinuousInterval = new RelayInterval(RelayIntervalMode.EveryFrame, 0),
                TargetHostIds = ["host-1"],
                IsContinuousEnabled = true,
            },
            Offset = new TimecodeOffset(false, 0, 0, 1, 0, FrameRate.Fps24),
            SourceSettings = new TimecodeSourceSettings
            {
                SourceType = TimecodeSourceType.Ltc,
                DeviceId = "device-123",
            },
        };
    }
}
