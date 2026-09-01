using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.ViewModels;

namespace TimecodeBridge;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Catch unhandled exceptions to prevent silent crashes
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"予期しないエラーが発生しました:\n\n{args.Exception.GetType().Name}: {args.Exception.Message}\n\n{args.Exception.StackTrace}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    $"致命的なエラーが発生しました:\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}",
                    "致命的エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };

        var services = new ServiceCollection();
        ServiceRegistration.ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
        Services = _serviceProvider;

        var mainWindow = new MainWindow
        {
            DataContext = _serviceProvider.GetRequiredService<MainViewModel>()
        };
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Stop and dispose TimecodeEngine before container disposal
        if (_serviceProvider != null)
        {
            var engine = _serviceProvider.GetService<ITimecodeEngine>();
            engine?.Stop();
            (engine as IDisposable)?.Dispose();
        }

        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
