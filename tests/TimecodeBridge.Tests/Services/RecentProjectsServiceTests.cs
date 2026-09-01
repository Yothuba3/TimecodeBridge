using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using System.IO;
using TimecodeBridge.Services;
using TimecodeBridge.Services.Interfaces;

namespace TimecodeBridge.Tests.Services;

public class RecentProjectsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppSettingsService _appSettings;
    private readonly RecentProjectsService _service;

    public RecentProjectsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"RecentProjectsServiceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var settingsPath = Path.Combine(_tempDir, "settings.json");
        _appSettings = new AppSettingsService(settingsPath);
        _service = new RecentProjectsService(_appSettings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void ImplementsIRecentProjectsService()
    {
        Assert.IsAssignableFrom<IRecentProjectsService>(_service);
    }

    [Fact]
    public void GetRecentProjects_Initially_ReturnsEmptyList()
    {
        var result = _service.GetRecentProjects();
        Assert.Empty(result);
    }

    [Fact]
    public void AddRecentProject_AddsToFront()
    {
        _service.AddRecentProject(@"C:\project1.json");
        _service.AddRecentProject(@"C:\project2.json");

        var result = _service.GetRecentProjects();

        Assert.Equal(2, result.Count);
        Assert.Equal(@"C:\project2.json", result[0]);
        Assert.Equal(@"C:\project1.json", result[1]);
    }

    [Fact]
    public void AddRecentProject_DuplicateMovesToFront()
    {
        _service.AddRecentProject(@"C:\project1.json");
        _service.AddRecentProject(@"C:\project2.json");
        _service.AddRecentProject(@"C:\project1.json");

        var result = _service.GetRecentProjects();

        Assert.Equal(2, result.Count);
        Assert.Equal(@"C:\project1.json", result[0]);
        Assert.Equal(@"C:\project2.json", result[1]);
    }

    [Fact]
    public void AddRecentProject_LimitsToMaxTen()
    {
        for (int i = 0; i < 15; i++)
        {
            _service.AddRecentProject($@"C:\project{i}.json");
        }

        var result = _service.GetRecentProjects();

        Assert.Equal(10, result.Count);
        Assert.Equal(@"C:\project14.json", result[0]);
    }

    [Fact]
    public void AddRecentProject_PersistsViaAppSettingsService()
    {
        _service.AddRecentProject(@"C:\persisted.json");

        // Create a new service instance using the same AppSettingsService
        var newService = new RecentProjectsService(_appSettings);
        var result = newService.GetRecentProjects();

        Assert.Single(result);
        Assert.Equal(@"C:\persisted.json", result[0]);
    }

    [Fact]
    public void Constructor_LoadsExistingProjects()
    {
        // Pre-populate via AppSettingsService
        _appSettings.SaveRecentProjects([@"C:\existing1.json", @"C:\existing2.json"]);

        var newService = new RecentProjectsService(_appSettings);
        var result = newService.GetRecentProjects();

        Assert.Equal(2, result.Count);
        Assert.Equal(@"C:\existing1.json", result[0]);
    }
}
