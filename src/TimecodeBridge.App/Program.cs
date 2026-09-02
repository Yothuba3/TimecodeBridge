using Avalonia;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TimecodeBridge.App.Services;

namespace TimecodeBridge.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // GUIアプリには標準エラーが無い。落ちた理由はファイルに残し、Windowsではメッセージボックスも出す
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Report("unhandled", e.ExceptionObject as Exception
                ?? new InvalidOperationException(e.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, e) => CrashLog.Write("unobserved-task", e.Exception);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Report("main", ex);
            Environment.Exit(1);
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static void Report(string stage, Exception ex)
    {
        var logPath = CrashLog.Write(stage, ex);
        if (!OperatingSystem.IsWindows()) return;

        var text = $"アプリケーションでエラーが発生しました。\n\n{ex.GetType().Name}: {ex.Message}\n\n" +
                   $"詳細は次のファイルに保存されています:\n{logPath ?? "(保存できませんでした)"}";
        MessageBoxW(IntPtr.Zero, text, "TimecodeBridge2", 0x10 /* MB_ICONERROR */);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
