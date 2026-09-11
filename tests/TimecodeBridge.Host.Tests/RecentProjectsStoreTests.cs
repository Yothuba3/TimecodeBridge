using System.Text.Json;
using TimecodeBridge.Host.Services;
using Xunit;

namespace TimecodeBridge.Host.Tests;

public class RecentProjectsStoreTests
{
    [Fact]
    public void AddsToFrontDeduplicatesAndCapsAtTen()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tcb3-recent-{Guid.NewGuid():N}.json");
        try
        {
            var store = new RecentProjectsStore(path, Path.Combine(Path.GetTempPath(), "none.json"));
            for (int i = 0; i < 12; i++) store.Add($"/p/show{i}.json");
            store.Add("/p/show3.json");
            Assert.Equal(RecentProjectsStore.Max, store.Items.Count);
            Assert.Equal("/p/show3.json", store.Items[0]);
            Assert.Equal(1, store.Items.Count(p => p.EndsWith("show3.json")));

            var reloaded = new RecentProjectsStore(path, Path.Combine(Path.GetTempPath(), "none.json"));
            Assert.Equal(store.Items, reloaded.Items);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ImportsLegacyV2SettingsWhenV3FileIsMissing()
    {
        var v3 = Path.Combine(Path.GetTempPath(), $"tcb3-recent-{Guid.NewGuid():N}.json");
        var v2 = Path.Combine(Path.GetTempPath(), $"tcb2-recent-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(v2, JsonSerializer.Serialize(new { recentProjects = new[] { "/old/a.json", "/old/b.json" } }));
            var store = new RecentProjectsStore(v3, v2);
            Assert.Equal(new[] { "/old/a.json", "/old/b.json" }, store.Items);
        }
        finally { File.Delete(v3); File.Delete(v2); }
    }
}
