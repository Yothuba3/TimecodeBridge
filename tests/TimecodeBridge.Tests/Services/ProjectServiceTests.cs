namespace TimecodeBridge.Tests.Services;

using System.IO;
using System.Text.Json;
using TimecodeBridge.Models;
using TimecodeBridge.Services;
using TimecodeBridge.Services.Interfaces;

public class ProjectServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ProjectService _service;

    public ProjectServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TimecodeBridge_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _service = new ProjectService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // --- 旧形式 cueOffset の移行 ---

    [Fact]
    public void LoadProject_LegacyCueOffset_MigratedToSendTimecode()
    {
        // 旧形式: cueOffset は「送信秒数の補正オフセット」だった。
        // 読込時に SendTimecode = TriggerTime + cueOffset へ変換される
        var data = CreateSampleProjectData();
        data.Cues[0].SendTriggerTimeAsSeconds = true;
        data.Cues[0].CueOffset = new TimecodeOffset(false, 0, 0, 30, 0, FrameRate.Fps24); // +30秒
        var path = Path.Combine(_tempDir, "legacy.json");
        _service.SaveProject(path, data);

        var loaded = _service.LoadProject(path);

        var cue = loaded.Cues[0];
        Assert.Null(cue.CueOffset);
        Assert.NotNull(cue.SendTimecode);
        // TriggerTime 00:01:00:00 + 30秒 = 00:01:30:00
        Assert.Equal(new TimecodeValue(0, 1, 30, 0, FrameRate.Fps24), cue.SendTimecode.Value);
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

    // --- SaveProject ---

    [Fact]
    public void SaveProject_CreatesJsonFile()
    {
        var filePath = Path.Combine(_tempDir, "test.json");
        var data = CreateSampleProjectData();

        _service.SaveProject(filePath, data);

        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public void SaveProject_SetsCurrentFilePath()
    {
        var filePath = Path.Combine(_tempDir, "test.json");
        var data = CreateSampleProjectData();

        _service.SaveProject(filePath, data);

        Assert.Equal(filePath, _service.CurrentFilePath);
    }

    [Fact]
    public void SaveProject_SetsHasUnsavedChangesToFalse()
    {
        var filePath = Path.Combine(_tempDir, "test.json");
        var data = CreateSampleProjectData();

        _service.MarkAsChanged();
        Assert.True(_service.HasUnsavedChanges);

        _service.SaveProject(filePath, data);

        Assert.False(_service.HasUnsavedChanges);
    }

    // --- LoadProject ---

    [Fact]
    public void LoadProject_ReadsJsonFileCorrectly()
    {
        var filePath = Path.Combine(_tempDir, "test.json");
        var original = CreateSampleProjectData();
        _service.SaveProject(filePath, original);

        var loaded = _service.LoadProject(filePath);

        Assert.Equal(original.Cues.Count, loaded.Cues.Count);
        Assert.Equal(original.Hosts.Count, loaded.Hosts.Count);
        Assert.Equal(original.RelaySettings.OscAddressPattern, loaded.RelaySettings.OscAddressPattern);
    }

    [Fact]
    public void LoadProject_SetsCurrentFilePath()
    {
        var filePath = Path.Combine(_tempDir, "test.json");
        _service.SaveProject(filePath, CreateSampleProjectData());

        _service.LoadProject(filePath);

        Assert.Equal(filePath, _service.CurrentFilePath);
    }

    [Fact]
    public void LoadProject_SetsHasUnsavedChangesToFalse()
    {
        var filePath = Path.Combine(_tempDir, "test.json");
        _service.SaveProject(filePath, CreateSampleProjectData());

        _service.MarkAsChanged();
        Assert.True(_service.HasUnsavedChanges);

        _service.LoadProject(filePath);

        Assert.False(_service.HasUnsavedChanges);
    }

    [Fact]
    public void LoadProject_NonExistentFile_ThrowsFileNotFoundException()
    {
        var filePath = Path.Combine(_tempDir, "nonexistent.json");

        Assert.Throws<FileNotFoundException>(() => _service.LoadProject(filePath));
    }

    // --- Round-trip ---

    [Fact]
    public void SaveAndLoad_RoundTrip_PreservesData()
    {
        var filePath = Path.Combine(_tempDir, "roundtrip.json");
        var original = CreateSampleProjectData();

        _service.SaveProject(filePath, original);
        var loaded = _service.LoadProject(filePath);

        Assert.Equal(original.Cues.Count, loaded.Cues.Count);
        Assert.Equal(original.Cues[0].Id, loaded.Cues[0].Id);
        Assert.Equal(original.Cues[0].Name, loaded.Cues[0].Name);
        Assert.Equal(original.Cues[0].TriggerTime, loaded.Cues[0].TriggerTime);
        Assert.Equal(original.Cues[0].OscAddress, loaded.Cues[0].OscAddress);
        Assert.Equal(original.Cues[0].Arguments.Count, loaded.Cues[0].Arguments.Count);

        Assert.Equal(original.Hosts[0].Id, loaded.Hosts[0].Id);
        Assert.Equal(original.Hosts[0].Name, loaded.Hosts[0].Name);
        Assert.Equal(original.Hosts[0].IpAddress, loaded.Hosts[0].IpAddress);
        Assert.Equal(original.Hosts[0].Port, loaded.Hosts[0].Port);

        Assert.Equal(original.RelaySettings.OscAddressPattern, loaded.RelaySettings.OscAddressPattern);
        Assert.Equal(original.RelaySettings.IsContinuousEnabled, loaded.RelaySettings.IsContinuousEnabled);

        Assert.Equal(original.Offset, loaded.Offset);

        Assert.Equal(original.SourceSettings.SourceType, loaded.SourceSettings.SourceType);
        Assert.Equal(original.SourceSettings.DeviceId, loaded.SourceSettings.DeviceId);
    }

    // --- MarkAsChanged / HasUnsavedChanges ---

    [Fact]
    public void HasUnsavedChanges_InitiallyFalse()
    {
        Assert.False(_service.HasUnsavedChanges);
    }

    [Fact]
    public void MarkAsChanged_SetsHasUnsavedChangesToTrue()
    {
        _service.MarkAsChanged();

        Assert.True(_service.HasUnsavedChanges);
    }

    // --- UnsavedChangesStatusChanged event ---

    [Fact]
    public void MarkAsChanged_RaisesUnsavedChangesStatusChangedEvent()
    {
        var eventRaised = false;
        _service.UnsavedChangesStatusChanged += (_, _) => eventRaised = true;

        _service.MarkAsChanged();

        Assert.True(eventRaised);
    }

    [Fact]
    public void SaveProject_RaisesUnsavedChangesStatusChangedEvent_WhenStatusChanges()
    {
        var filePath = Path.Combine(_tempDir, "test.json");
        _service.MarkAsChanged();

        var eventRaised = false;
        _service.UnsavedChangesStatusChanged += (_, _) => eventRaised = true;

        _service.SaveProject(filePath, CreateSampleProjectData());

        Assert.True(eventRaised);
    }

    [Fact]
    public void MarkAsChanged_DoesNotRaiseEvent_WhenAlreadyChanged()
    {
        _service.MarkAsChanged();

        var eventRaised = false;
        _service.UnsavedChangesStatusChanged += (_, _) => eventRaised = true;

        _service.MarkAsChanged();

        Assert.False(eventRaised);
    }

    // --- CurrentFilePath ---

    [Fact]
    public void CurrentFilePath_InitiallyNull()
    {
        Assert.Null(_service.CurrentFilePath);
    }
}
