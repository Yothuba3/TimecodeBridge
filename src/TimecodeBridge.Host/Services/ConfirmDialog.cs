using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace TimecodeBridge.Host.Services;

/// <summary>「はい / いいえ」だけの最小モーダル。未保存確認など、Web 側の状態に依存させたくない確認に使う。</summary>
public static class ConfirmDialog
{
    public static async Task<bool> ShowAsync(Window owner, string title, string message, string yes = "破棄して続行", string no = "キャンセル")
    {
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var yesButton = new Button { Content = yes, MinWidth = 120 };
        var noButton = new Button { Content = no, MinWidth = 120, IsDefault = true, IsCancel = true };
        yesButton.Click += (_, _) => dialog.Close(true);
        noButton.Click += (_, _) => dialog.Close(false);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { noButton, yesButton } },
            },
        };
        return await dialog.ShowDialog<bool?>(owner) == true;
    }
}
