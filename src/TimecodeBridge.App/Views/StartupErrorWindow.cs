using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TimecodeBridge.App.Views;

/// <summary>
/// 初期化に失敗したときにメインウィンドウの代わりに出す画面。
/// 例外の内容とログの保存先を表示し、ユーザーがコピーして報告できるようにする。
/// </summary>
public static class StartupErrorWindow
{
    public static Window Create(Exception exception, string? logPath)
    {
        var window = new Window
        {
            Title = "TimecodeBridge2 起動エラー",
            Width = 720,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };

        var heading = new TextBlock
        {
            Text = "アプリケーションの初期化中にエラーが発生しました。",
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var logInfo = new TextBlock
        {
            Text = logPath is null
                ? "エラーログは保存できませんでした。"
                : $"この内容は次のファイルにも保存されています:\n{logPath}",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var detail = new TextBox
        {
            Text = exception.ToString(),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Menlo, Consolas, monospace"),
            FontSize = 12,
        };
        var close = new Button
        {
            Content = "閉じる",
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 100,
            Margin = new Thickness(0, 12, 0, 0),
        };
        close.Click += (_, _) => window.Close();

        DockPanel.SetDock(heading, Dock.Top);
        DockPanel.SetDock(logInfo, Dock.Top);
        DockPanel.SetDock(close, Dock.Bottom);
        window.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children = { heading, logInfo, close, detail },
        };
        return window;
    }
}
