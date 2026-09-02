using TimecodeBridge.App.Services;

namespace TimecodeBridge.App.Tests.Services;

public class AppSettingsLegacyImportTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"tcb2-test-{Guid.NewGuid():N}");
    private readonly string _newPath;
    private readonly string _legacyPath;

    public AppSettingsLegacyImportTests()
    {
        _newPath = Path.Combine(_tempDir, "TimecodeBridge2", "settings.json");
        _legacyPath = Path.Combine(_tempDir, "TimecodeBridge", "settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private void WriteSettings(string path, params string[] recentProjects)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $$"""{"RecentProjects":[{{string.Join(",", recentProjects.Select(p => $"\"{p}\""))}}]}""");
    }

    [Fact]
    public void 新設定が無く旧設定があれば初回に引き継ぐ()
    {
        WriteSettings(_legacyPath, "/shows/a.json", "/shows/b.json");

        var service = new AppSettingsService(_newPath, _legacyPath);

        Assert.True(File.Exists(_newPath));
        Assert.Equal(["/shows/a.json", "/shows/b.json"], service.LoadRecentProjects());
    }

    [Fact]
    public void 新設定が既にあれば旧設定は読まない()
    {
        WriteSettings(_legacyPath, "/shows/old.json");
        WriteSettings(_newPath, "/shows/new.json");

        var service = new AppSettingsService(_newPath, _legacyPath);

        Assert.Equal(["/shows/new.json"], service.LoadRecentProjects());
    }

    [Fact]
    public void 引き継ぎ後の保存は新設定にだけ書く()
    {
        WriteSettings(_legacyPath, "/shows/a.json");
        var service = new AppSettingsService(_newPath, _legacyPath);

        service.SaveRecentProjects(["/shows/c.json"]);

        Assert.Equal(["/shows/c.json"], new AppSettingsService(_newPath, _legacyPath).LoadRecentProjects());
        Assert.Contains("/shows/a.json", File.ReadAllText(_legacyPath));
    }

    [Fact]
    public void どちらも無ければ何も作らない()
    {
        var service = new AppSettingsService(_newPath, _legacyPath);

        Assert.False(File.Exists(_newPath));
        Assert.Empty(service.LoadRecentProjects());
    }
}
