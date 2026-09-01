using System.IO;
using TimecodeBridge.App.Services;

namespace TimecodeBridge.App.Tests.Services;

public class RecentProjectsServiceTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"tcb-test-{Guid.NewGuid():N}", "settings.json");

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_settingsPath);
        if (dir is not null && Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void AddRecentProject_MRU順で保持し最大10件に収める()
    {
        var service = new RecentProjectsService(new AppSettingsService(_settingsPath));

        for (int i = 0; i < 12; i++) service.AddRecentProject($"/p/{i}.json");
        service.AddRecentProject("/p/5.json");

        var recent = service.GetRecentProjects();
        Assert.Equal(10, recent.Count);
        Assert.Equal("/p/5.json", recent[0]);
        Assert.Equal("/p/11.json", recent[1]);
        Assert.DoesNotContain("/p/0.json", recent);
    }

    [Fact]
    public void AddRecentProject_設定ファイルに永続化され別インスタンスから読める()
    {
        new RecentProjectsService(new AppSettingsService(_settingsPath)).AddRecentProject("/p/a.json");

        var reloaded = new RecentProjectsService(new AppSettingsService(_settingsPath));

        Assert.True(File.Exists(_settingsPath));
        Assert.Equal(["/p/a.json"], reloaded.GetRecentProjects());
    }
}
