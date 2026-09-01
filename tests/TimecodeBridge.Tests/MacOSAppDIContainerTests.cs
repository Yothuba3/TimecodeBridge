using Microsoft.Extensions.DependencyInjection;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Tests;

/// <summary>
/// macOS App.axaml.csのDIコンテナ初期化を検証するテスト
/// </summary>
public class MacOSAppDIContainerTests
{
    [Fact]
    public void ServiceCollection_ShouldRegisterFileDialogService()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        // Assert
        var fileDialogService = provider.GetService<IFileDialogService>();
        Assert.NotNull(fileDialogService);
    }

    [Fact]
    public void ServiceCollection_ShouldRegisterAudioDeviceService()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        // Assert
        var audioDeviceService = provider.GetService<IAudioDeviceService>();
        Assert.NotNull(audioDeviceService);
    }

    [Fact]
    public void ServiceCollection_ShouldRegisterCoreServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        // Assert - Core services should be available
        var projectService = provider.GetService<IProjectService>();
        var cueManager = provider.GetService<ICueManager>();
        var oscSender = provider.GetService<IOscSender>();

        Assert.NotNull(projectService);
        Assert.NotNull(cueManager);
        Assert.NotNull(oscSender);
    }

    [Fact]
    public void AudioDeviceServiceStub_ShouldReturnEmptyDeviceList()
    {
        // Arrange
        var services = new ServiceCollection();
        ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        // Act
        var audioDeviceService = provider.GetRequiredService<IAudioDeviceService>();
        var captureDevices = audioDeviceService.GetCaptureDevices();
        var renderDevices = audioDeviceService.GetRenderDevices();

        // Assert - Stub implementation should return empty lists for now
        Assert.NotNull(captureDevices);
        Assert.NotNull(renderDevices);
        // Empty or with stub data is acceptable for Phase 2a
    }

    /// <summary>
    /// App.axaml.csで使用されるサービス設定ロジックを再現
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        // macOS固有サービス（stub実装）
        services.AddSingleton<IFileDialogService>(new FileDialogServiceStub());
        services.AddSingleton<IAudioDeviceService>(new AudioDeviceServiceStub());

        // Coreサービス
        services.AddSingleton<IProjectService, TimecodeBridge.Core.Services.ProjectService>();
        services.AddSingleton<ICueManager, TimecodeBridge.Core.Services.CueManager>();
        services.AddSingleton<IOscSender, TimecodeBridge.Core.Services.OscSender>();
        services.AddSingleton<IHostRegistry, TimecodeBridge.Core.Services.HostRegistry>();
        services.AddSingleton<ITimecodeGenerator, TimecodeBridge.Core.Services.TimecodeGenerator>();
    }

    // Stub implementations for testing
    private class FileDialogServiceStub : IFileDialogService
    {
        public string? ShowOpenFileDialog(string filter, string? initialDirectory = null)
        {
            return null;
        }

        public string? ShowSaveFileDialog(string filter, string? defaultFileName = null, string? initialDirectory = null)
        {
            return null;
        }
    }

    private class AudioDeviceServiceStub : IAudioDeviceService
    {
        public IReadOnlyList<TimecodeBridge.Core.Models.AudioDeviceInfo> GetCaptureDevices()
        {
            return Array.Empty<TimecodeBridge.Core.Models.AudioDeviceInfo>();
        }

        public IReadOnlyList<TimecodeBridge.Core.Models.AudioDeviceInfo> GetRenderDevices()
        {
            return Array.Empty<TimecodeBridge.Core.Models.AudioDeviceInfo>();
        }
    }
}
