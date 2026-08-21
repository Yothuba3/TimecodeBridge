using Microsoft.Extensions.DependencyInjection;
using TimecodeBridge.Models;
using TimecodeBridge.Services;
using TimecodeBridge.Services.Interfaces;
using TimecodeBridge.ViewModels;

namespace TimecodeBridge;

public static class ServiceRegistration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        // Services (Singleton)
        services.AddSingleton<IHostRegistry, HostRegistry>();
        services.AddSingleton<ITimecodeEngine>(_ => new TimecodeEngine(FrameRate.Fps30));
        services.AddSingleton<ITimecodeGenerator, TimecodeGenerator>();
        services.AddSingleton<ILtcEncoder, LtcEncoder>();

        services.AddSingleton<IOscTransport, OscTransport>();
        services.AddSingleton<IOscSender, OscSender>();
        services.AddSingleton<ICueManager, CueManager>();
        services.AddSingleton<IOscTriggerPanelManager, OscTriggerPanelManager>();
        services.AddSingleton<ITimecodeRelay, TimecodeRelay>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IRecentProjectsService, RecentProjectsService>();

        // Dialog Services (Singleton)
        services.AddSingleton<ICueDialogService, CueDialogService>();
        services.AddSingleton<IHostDialogService, HostDialogService>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IOscTriggerDialogService, OscTriggerDialogService>();

        // ViewModels
        services.AddSingleton<CueSyncViewModel>();
        services.AddSingleton<TimecodeViewModel>();
        services.AddSingleton<CueListViewModel>();
        services.AddSingleton<RelayViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<HostManagerViewModel>();
        services.AddSingleton<OscTriggerPanelViewModel>();
        services.AddTransient<LogViewModel>();
        services.AddTransient<AudioWaveformViewModel>();
    }
}
