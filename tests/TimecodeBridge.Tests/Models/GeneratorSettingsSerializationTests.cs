using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
namespace TimecodeBridge.Tests.Models;

using System.Text.Json;
using TimecodeBridge.Core.Models;

public class GeneratorSettingsSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = ProjectData.CreateJsonOptions();

    [Fact]
    public void GeneratorSettings_RoundTrip()
    {
        var original = new GeneratorSettings
        {
            FrameRate = FrameRate.Fps2997Drop,
            StartTime = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps2997Drop),
            OutputDeviceId = "test-device-id",
            VolumeLevel = 0.6f,
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GeneratorSettings>(json, JsonOptions)!;

        Assert.Equal(original.FrameRate, deserialized.FrameRate);
        Assert.Equal(original.StartTime, deserialized.StartTime);
        Assert.Equal(original.OutputDeviceId, deserialized.OutputDeviceId);
        Assert.Equal(original.VolumeLevel, deserialized.VolumeLevel);
    }

    [Fact]
    public void TimecodeSourceSettings_WithGenerator_RoundTrip()
    {
        var original = new TimecodeSourceSettings
        {
            SourceType = TimecodeSourceType.Generator,
            DeviceId = "",
            GeneratorSettings = new GeneratorSettings
            {
                FrameRate = FrameRate.Fps25,
                StartTime = new TimecodeValue(0, 30, 0, 0, FrameRate.Fps25),
                OutputDeviceId = "output-device",
                VolumeLevel = 0.9f,
            },
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<TimecodeSourceSettings>(json, JsonOptions)!;

        Assert.Equal(TimecodeSourceType.Generator, deserialized.SourceType);
        Assert.Equal(FrameRate.Fps25, deserialized.GeneratorSettings.FrameRate);
        Assert.Equal(new TimecodeValue(0, 30, 0, 0, FrameRate.Fps25), deserialized.GeneratorSettings.StartTime);
        Assert.Equal("output-device", deserialized.GeneratorSettings.OutputDeviceId);
        Assert.Equal(0.9f, deserialized.GeneratorSettings.VolumeLevel);
    }

    [Fact]
    public void ProjectData_WithGeneratorSettings_RoundTrip()
    {
        var original = new ProjectData
        {
            SourceSettings = new TimecodeSourceSettings
            {
                SourceType = TimecodeSourceType.Generator,
                GeneratorSettings = new GeneratorSettings
                {
                    FrameRate = FrameRate.Fps30,
                    StartTime = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30),
                    OutputDeviceId = "device-guid",
                    VolumeLevel = 0.8f,
                },
            },
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<ProjectData>(json, JsonOptions)!;

        Assert.Equal(TimecodeSourceType.Generator, deserialized.SourceSettings.SourceType);
        Assert.Equal(FrameRate.Fps30, deserialized.SourceSettings.GeneratorSettings.FrameRate);
        Assert.Equal(0.8f, deserialized.SourceSettings.GeneratorSettings.VolumeLevel);
    }

    [Fact]
    public void GeneratorSettings_DefaultValues()
    {
        var settings = new GeneratorSettings();

        Assert.Equal(FrameRate.Fps30, settings.FrameRate);
        Assert.Equal(new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30), settings.StartTime);
        Assert.Equal(string.Empty, settings.OutputDeviceId);
        Assert.Equal(0.8f, settings.VolumeLevel);
    }
}
