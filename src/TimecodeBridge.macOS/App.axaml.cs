using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.macOS.Services;
using TimecodeBridge.macOS.Services.CoreAudio;
using TimecodeBridge.macOS.ViewModels;
using TimecodeBridge.Services.Interfaces;

namespace TimecodeBridge.macOS;

public partial class App : Application
{
    public IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = new MainWindow
                {
                    DataContext = Services.GetRequiredService<MainViewModel>()
                };
                desktop.MainWindow = mainWindow;
                desktop.Exit += (_, _) => (Services as IDisposable)?.Dispose();
            }

            base.OnFrameworkInitializationCompleted();
        }
        catch (Exception ex)
        {
            ShowInitializationErrorAndExit(ex);
        }
    }

    /// <summary>
    /// DIコンテナにサービスを登録
    /// </summary>
    public static void ConfigureServices(IServiceCollection services)
    {
        // macOS固有サービス
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IAudioDeviceService, CoreAudioDeviceService>();
        services.AddSingleton<ITimecodeEngine>(sp => new TimecodeEngine(
            FrameRate.Fps30,
            sp.GetRequiredService<IAudioDeviceService>(),
            () => new CoreAudioCapture(),
            () => new CoreAudioPlayback()));
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IRecentProjectsService, RecentProjectsService>();
        services.AddSingleton<ICueDialogService, CueDialogService>();
        services.AddSingleton<IHostDialogService, HostDialogService>();
        services.AddSingleton<IOscTriggerDialogService, OscTriggerDialogService>();

        // Core共通サービス
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IHostRegistry, HostRegistry>();
        services.AddSingleton<IOscTransport, OscTransport>();
        services.AddSingleton<IOscSender, OscSender>();
        services.AddSingleton<ICueManager, CueManager>();
        services.AddSingleton<IOscTriggerPanelManager, OscTriggerPanelManager>();
        services.AddSingleton<ITimecodeRelay, TimecodeRelay>();
        services.AddSingleton<ITimecodeGenerator, TimecodeGenerator>();

        // ViewModels
        services.AddSingleton<CueSyncViewModel>();
        services.AddSingleton<TimecodeViewModel>();
        services.AddSingleton<CueListViewModel>();
        services.AddSingleton<HostManagerViewModel>();
        services.AddSingleton<RelayViewModel>();
        services.AddSingleton<OscTriggerPanelViewModel>();
        services.AddSingleton<AudioWaveformViewModel>();
        services.AddSingleton<LogViewModel>();
        services.AddSingleton<MainViewModel>();
    }

    /// <summary>
    /// 初期化エラーをコンソールに出力して終了
    /// </summary>
    private static void ShowInitializationErrorAndExit(Exception ex)
    {
        Console.Error.WriteLine($"[FATAL] アプリケーションの初期化中にエラーが発生しました。\n\n詳細: {ex.Message}");
        Console.Error.WriteLine(ex.StackTrace);
        Environment.Exit(1);
    }
}
