using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Microsoft.Extensions.DependencyInjection;
using TimecodeBridge.Core.Audio;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.Host.Bridge;
using TimecodeBridge.Host.Services;
using TimecodeBridge.Ltc;
using TimecodeBridge.Mac;
using TimecodeBridge.Windows.Services;

namespace TimecodeBridge.Host;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                var services = new ServiceCollection();
                ConfigureServices(services);
                var provider = services.BuildServiceProvider();
                desktop.MainWindow = provider.GetRequiredService<MainWindow>();
                var selfTest = DevSelfTest.StartIfRequested(provider.GetRequiredService<IAudioDeviceService>());
                desktop.Exit += (_, _) => { selfTest?.Dispose(); provider.Dispose(); };
            }
            catch (Exception ex)
            {
                desktop.MainWindow = StartupErrorWindow(ex);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    public static void ConfigureServices(IServiceCollection services)
    {
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IAudioDeviceService, WindowsAudioDeviceService>();
            services.AddSingleton<ITimecodeEngine>(sp => new TimecodeEngine(
                FrameRate.Fps30,
                sp.GetRequiredService<IAudioDeviceService>(),
                () => new WasapiAudioCapture(),
                () => new WasapiAudioPlayback(),
                () => new LibltcDecoder()));
        }
        else
        {
            services.AddSingleton<IAudioDeviceService, CoreAudioDeviceService>();
            services.AddSingleton<ITimecodeEngine>(sp => new TimecodeEngine(
                FrameRate.Fps30,
                sp.GetRequiredService<IAudioDeviceService>(),
                () => new CoreAudioCapture(),
                () => new CoreAudioPlayback(),
                () => new LibltcDecoder()));
        }

        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IHostRegistry, HostRegistry>();
        services.AddSingleton<IOscTransport, OscTransport>();
        services.AddSingleton<IOscSender, OscSender>();
        services.AddSingleton<ICueManager, CueManager>();
        services.AddSingleton<IOscTriggerPanelManager, OscTriggerPanelManager>();
        services.AddSingleton<ITimecodeRelay, TimecodeRelay>();

        services.AddSingleton<HostState>();
        services.AddSingleton<ProjectCoordinator>();
        services.AddSingleton<CommandRouter>();
        services.AddSingleton<MainWindow>();
    }

    private static Window StartupErrorWindow(Exception ex)
    {
        string? logPath = null;
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            logPath = Path.Combine(AppPaths.DataDirectory, "startup-error.log");
            File.WriteAllText(logPath, $"{DateTime.Now:O}\n{ex}\n");
        }
        catch { /* ログが書けなくても起動エラー表示は続ける */ }

        return new Window
        {
            Title = "TimecodeBridge — 起動エラー",
            Width = 720,
            Height = 420,
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Margin = new Thickness(16),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Text = $"起動に失敗しました。\n\n{ex.Message}\n\n{(logPath is null ? "" : $"詳細: {logPath}\n\n")}{ex}",
                },
            },
        };
    }
}
