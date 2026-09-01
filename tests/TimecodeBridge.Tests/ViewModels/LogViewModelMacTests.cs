namespace TimecodeBridge.Tests.ViewModels;

using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.macOS.ViewModels;

// --- Stub ---

internal class StubFileDialogServiceForLog : IFileDialogService
{
    public string? NextSaveFileDialogResult { get; set; }

    public string? ShowOpenFileDialog(string filter, string? initialDirectory = null) => null;

    public string? ShowSaveFileDialog(string filter, string? defaultFileName = null, string? initialDirectory = null)
    {
        return NextSaveFileDialogResult;
    }
}

// --- Tests ---

public class LogViewModelMacTests
{
    private readonly StubFileDialogServiceForLog _fileDialogService = new();

    private LogViewModel CreateVm() => new(_fileDialogService);

    // --- Initial state ---

    [Fact]
    public void Constructor_InitializesEmptyLogs()
    {
        var vm = CreateVm();
        Assert.NotNull(vm.LogEntries);
        Assert.Empty(vm.LogEntries);
        Assert.Equal("0件", vm.LogCountDisplay);
    }

    // --- AddLog tests ---

    [Fact]
    public void AddLog_AddsLogEntry()
    {
        var vm = CreateVm();

        vm.AddLog(LogLevel.Info, "Test message");

        Assert.Single(vm.LogEntries);
        Assert.Equal(LogLevel.Info, vm.LogEntries[0].Level);
        Assert.Equal("Test message", vm.LogEntries[0].Message);
    }

    [Fact]
    public void AddLog_SetsTimestamp()
    {
        var vm = CreateVm();

        vm.AddLog(LogLevel.Info, "Test");

        Assert.NotNull(vm.LogEntries[0].Timestamp);
        Assert.NotEmpty(vm.LogEntries[0].Timestamp);
    }

    [Fact]
    public void AddLog_UpdatesLogCountDisplay()
    {
        var vm = CreateVm();

        vm.AddLog(LogLevel.Info, "First");
        Assert.Equal("1件", vm.LogCountDisplay);

        vm.AddLog(LogLevel.Warning, "Second");
        Assert.Equal("2件", vm.LogCountDisplay);
    }

    [Fact]
    public void AddLog_NewestEntryIsFirst()
    {
        var vm = CreateVm();

        vm.AddLog(LogLevel.Info, "First");
        vm.AddLog(LogLevel.Warning, "Second");
        vm.AddLog(LogLevel.Error, "Third");

        Assert.Equal("Third", vm.LogEntries[0].Message);
        Assert.Equal("Second", vm.LogEntries[1].Message);
        Assert.Equal("First", vm.LogEntries[2].Message);
    }

    [Fact]
    public void AddLog_DifferentLogLevels()
    {
        var vm = CreateVm();

        vm.AddLog(LogLevel.Info, "Info message");
        vm.AddLog(LogLevel.Warning, "Warning message");
        vm.AddLog(LogLevel.Error, "Error message");

        Assert.Equal(3, vm.LogEntries.Count);
        Assert.Equal(LogLevel.Error, vm.LogEntries[0].Level);
        Assert.Equal(LogLevel.Warning, vm.LogEntries[1].Level);
        Assert.Equal(LogLevel.Info, vm.LogEntries[2].Level);
    }

    // --- Circular buffer tests ---

    [Fact]
    public void CircularBuffer_DoesNotExceed1000Entries()
    {
        var vm = CreateVm();

        for (int i = 0; i < 1005; i++)
        {
            vm.AddLog(LogLevel.Info, $"Message {i}");
        }

        Assert.Equal(1000, vm.LogEntries.Count);
        Assert.Equal("1000件", vm.LogCountDisplay);
    }

    [Fact]
    public void CircularBuffer_RemovesOldestFirst()
    {
        var vm = CreateVm();

        for (int i = 0; i < 1005; i++)
        {
            vm.AddLog(LogLevel.Info, $"Message {i}");
        }

        // The oldest entries (0-4) should have been removed
        // Since newest is first, the first entry should be Message 1004
        Assert.Contains("Message 1004", vm.LogEntries[0].Message);
        // The last entry should be Message 5 (oldest kept)
        Assert.Contains("Message 5", vm.LogEntries[999].Message);
    }

    // --- ClearCommand tests ---

    [Fact]
    public void ClearCommand_ClearsAllLogs()
    {
        var vm = CreateVm();

        vm.AddLog(LogLevel.Info, "First");
        vm.AddLog(LogLevel.Warning, "Second");
        vm.AddLog(LogLevel.Error, "Third");

        vm.ClearCommand.Execute(null);

        // Clear adds an Info message, so count is 1
        Assert.Single(vm.LogEntries);
        Assert.Contains("ログをクリアしました", vm.LogEntries[0].Message);
    }

    [Fact]
    public void ClearCommand_UpdatesLogCountDisplay()
    {
        var vm = CreateVm();

        vm.AddLog(LogLevel.Info, "Test");
        vm.ClearCommand.Execute(null);

        Assert.Equal("1件", vm.LogCountDisplay); // 1 because Clear adds a message
    }

    [Fact]
    public void ClearCommand_OnEmptyLogs_NoError()
    {
        var vm = CreateVm();

        vm.ClearCommand.Execute(null);

        Assert.Single(vm.LogEntries); // Clear message added
    }

    // --- LogEntryViewModel tests ---

    [Fact]
    public void LogEntryViewModel_LevelDisplay_Info()
    {
        var entry = new LogEntryViewModel
        {
            Level = LogLevel.Info,
            Message = "Test",
            Timestamp = "12:00:00"
        };

        Assert.Equal("INFO", entry.LevelDisplay);
    }

    [Fact]
    public void LogEntryViewModel_LevelDisplay_Warning()
    {
        var entry = new LogEntryViewModel
        {
            Level = LogLevel.Warning,
            Message = "Test",
            Timestamp = "12:00:00"
        };

        Assert.Equal("WARN", entry.LevelDisplay);
    }

    [Fact]
    public void LogEntryViewModel_LevelDisplay_Error()
    {
        var entry = new LogEntryViewModel
        {
            Level = LogLevel.Error,
            Message = "Test",
            Timestamp = "12:00:00"
        };

        Assert.Equal("ERROR", entry.LevelDisplay);
    }

    // --- ExportCommand tests ---

    [Fact]
    public async Task ExportCommand_WhenUserCancels_NoFileCreated()
    {
        var vm = CreateVm();
        _fileDialogService.NextSaveFileDialogResult = null;

        vm.AddLog(LogLevel.Info, "Test");
        await vm.ExportCommand.ExecuteAsync(null);

        // Should only have the original log entry, no export success message
        Assert.Single(vm.LogEntries);
    }

    [Fact]
    public async Task ExportCommand_WhenUserSelectsFile_CreatesFile()
    {
        var vm = CreateVm();
        var tempPath = Path.Combine(Path.GetTempPath(), $"test_log_{Guid.NewGuid()}.txt");
        _fileDialogService.NextSaveFileDialogResult = tempPath;

        try
        {
            vm.AddLog(LogLevel.Info, "Test message");
            await vm.ExportCommand.ExecuteAsync(null);

            Assert.True(File.Exists(tempPath));
            var lines = await File.ReadAllLinesAsync(tempPath);
            Assert.NotEmpty(lines);
            Assert.Contains("Test message", lines[0]);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task ExportCommand_ExportsAllLogs()
    {
        var vm = CreateVm();
        var tempPath = Path.Combine(Path.GetTempPath(), $"test_log_{Guid.NewGuid()}.txt");
        _fileDialogService.NextSaveFileDialogResult = tempPath;

        try
        {
            vm.AddLog(LogLevel.Info, "First message");
            vm.AddLog(LogLevel.Warning, "Second message");
            vm.AddLog(LogLevel.Error, "Third message");

            await vm.ExportCommand.ExecuteAsync(null);

            var lines = await File.ReadAllLinesAsync(tempPath);
            // 3 original + 1 export success message = 4 total
            // But we're exporting the logs before the success message is added
            Assert.Equal(3, lines.Length);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task ExportCommand_AddsSuccessLogEntry()
    {
        var vm = CreateVm();
        var tempPath = Path.Combine(Path.GetTempPath(), $"test_log_{Guid.NewGuid()}.txt");
        _fileDialogService.NextSaveFileDialogResult = tempPath;

        try
        {
            vm.AddLog(LogLevel.Info, "Test");
            await vm.ExportCommand.ExecuteAsync(null);

            // Should have original + export success message
            Assert.Equal(2, vm.LogEntries.Count);
            Assert.Contains("ログをエクスポートしました", vm.LogEntries[0].Message);
            Assert.Equal(LogLevel.Info, vm.LogEntries[0].Level);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task ExportCommand_OnError_AddsErrorLogEntry()
    {
        var vm = CreateVm();
        // Use an invalid path to trigger an error
        _fileDialogService.NextSaveFileDialogResult = "/invalid/path/that/does/not/exist/test.txt";

        vm.AddLog(LogLevel.Info, "Test");
        await vm.ExportCommand.ExecuteAsync(null);

        // Should have original + error message
        Assert.Equal(2, vm.LogEntries.Count);
        Assert.Contains("ログエクスポート失敗", vm.LogEntries[0].Message);
        Assert.Equal(LogLevel.Error, vm.LogEntries[0].Level);
    }

    // --- LogLevelToBrushConverter tests ---

    [Fact]
    public void LogLevelToBrushConverter_Info_ReturnsLightBlue()
    {
        var converter = LogLevelToBrushConverter.Instance;
        var result = converter.Convert(new object[] { LogLevel.Info }, typeof(object), null, null);

        Assert.Equal(Avalonia.Media.Brushes.LightBlue, result);
    }

    [Fact]
    public void LogLevelToBrushConverter_Warning_ReturnsOrange()
    {
        var converter = LogLevelToBrushConverter.Instance;
        var result = converter.Convert(new object[] { LogLevel.Warning }, typeof(object), null, null);

        Assert.Equal(Avalonia.Media.Brushes.Orange, result);
    }

    [Fact]
    public void LogLevelToBrushConverter_Error_ReturnsRed()
    {
        var converter = LogLevelToBrushConverter.Instance;
        var result = converter.Convert(new object[] { LogLevel.Error }, typeof(object), null, null);

        Assert.Equal(Avalonia.Media.Brushes.Red, result);
    }

    [Fact]
    public void LogLevelToBrushConverter_EmptyValues_ReturnsGray()
    {
        var converter = LogLevelToBrushConverter.Instance;
        var result = converter.Convert(Array.Empty<object>(), typeof(object), null, null);

        Assert.Equal(Avalonia.Media.Brushes.Gray, result);
    }
}
