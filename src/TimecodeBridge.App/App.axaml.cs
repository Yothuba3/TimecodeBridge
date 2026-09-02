using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.App.Services;
using TimecodeBridge.App.Services.CoreAudio;
using TimecodeBridge.App.Views;
using TimecodeBridge.Windows.Services;
using TimecodeBridge.App.ViewModels;
using TimecodeBridge.Services.Interfaces;

namespace TimecodeBridge.App;

public partial class App : Application
{
    public IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = CreateMainWindow(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// DIを組み立ててメインウィンドウを作る。失敗しても落とさず、原因を表示するウィンドウを返す
    /// （GUIアプリには標準エラーが無いので、黙って終了すると手がかりが残らない）。
    /// </summary>
    private Window CreateMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();
            desktop.Exit += (_, _) => (Services as IDisposable)?.Dispose();

            return new MainWindow
            {
                DataContext = Services.GetRequiredService<MainViewModel>()
            };
        }
        catch (Exception ex)
        {
            var logPath = CrashLog.Write("initialization", ex);
            return StartupErrorWindow.Create(ex, logPath);
        }
    }

    /// <summary>
    /// DIコンテナにサービスを登録
    /// </summary>
    public static void ConfigureServices(IServiceCollection services)
    {
        // プラットフォーム別のオーディオ実装（UI層は共通）
        services.AddSingleton<IFileDialogService, FileDialogService>();
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IAudioDeviceService, WindowsAudioDeviceService>();
            services.AddSingleton<ITimecodeEngine>(_ => new WasapiTimecodeEngine(FrameRate.Fps30));
        }
        else
        {
            services.AddSingleton<IAudioDeviceService, CoreAudioDeviceService>();
            services.AddSingleton<ITimecodeEngine>(sp => new TimecodeEngine(
                FrameRate.Fps30,
                sp.GetRequiredService<IAudioDeviceService>(),
                () => new CoreAudioCapture(),
                () => new CoreAudioPlayback()));
        }
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
}
