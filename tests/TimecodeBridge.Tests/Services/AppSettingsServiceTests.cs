using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
namespace TimecodeBridge.Tests.Services;

using System.IO;
using TimecodeBridge.Services;

public class AppSettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsFilePath;

    public AppSettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TimecodeBridge_AppSettings_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _settingsFilePath = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // --- LoadRecentProjects ---

    [Fact]
    public void LoadRecentProjects_ファイルなし_空リストを返す()
    {
        var service = new AppSettingsService(_settingsFilePath);

        var result = service.LoadRecentProjects();

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void SaveAndLoadRecentProjects_ラウンドトリップ()
    {
        var service = new AppSettingsService(_settingsFilePath);
        var projects = new List<string> { @"C:\project1.json", @"C:\project2.json" };

        service.SaveRecentProjects(projects);
        var loaded = service.LoadRecentProjects();

        Assert.Equal(2, loaded.Count);
        Assert.Equal(projects[0], loaded[0]);
        Assert.Equal(projects[1], loaded[1]);
    }

    // --- 破損JSONファイル ---

    [Fact]
    public void LoadRecentProjects_破損JSON_空リストにフォールバック()
    {
        File.WriteAllText(_settingsFilePath, "{ this is not valid json!!!");
        var service = new AppSettingsService(_settingsFilePath);

        var result = service.LoadRecentProjects();

        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
