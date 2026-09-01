using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Threading;

namespace TimecodeBridge.macOS.Services;

/// <summary>
/// Avaloniaの非同期ダイアログを同期APIのダイアログサービスから呼び出すためのヘルパー
/// </summary>
internal static class ModalDialog
{
    public static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    /// <summary>
    /// ダイアログを表示し、閉じるまで呼び出し元をブロックする。
    /// UIスレッドから呼ばれた場合はネストしたディスパッチャフレームでUIを動かし続ける
    /// (InvokeAsync().GetResult() だとUIスレッドがデッドロックするため)。
    /// メインウィンドウが無い(ヘッドレス)場合は default を返す。
    /// </summary>
    public static T? Show<T>(Func<Window, Task<T>> showDialog)
    {
        var owner = MainWindow;
        if (owner is null) return default;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            return Dispatcher.UIThread.InvokeAsync(() => showDialog(owner)).GetAwaiter().GetResult();
        }

        var task = showDialog(owner);
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.UIThread.PushFrame(frame);
        }

        return task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// OKボタンのみのメッセージダイアログを表示する
    /// </summary>
    public static async Task ShowMessageAsync(Window owner, string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true,
        };
        okButton.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 15,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                okButton,
            },
        };

        await dialog.ShowDialog(owner);
    }
}
