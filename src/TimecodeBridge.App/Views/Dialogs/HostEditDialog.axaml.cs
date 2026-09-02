using Avalonia.Controls;
using Avalonia.Interactivity;
using TimecodeBridge.Core.Models;
using TimecodeBridge.App.Services;

namespace TimecodeBridge.App.Views.Dialogs;

/// <summary>
/// OSCホスト編集ダイアログ。OKで編集結果の OscHost、キャンセルで null を返す。
/// </summary>
public partial class HostEditDialog : Window
{
    public HostEditDialog() : this(new OscHost { Id = string.Empty, Name = string.Empty, IpAddress = string.Empty, Port = 53000 })
    {
    }

    public HostEditDialog(OscHost host)
    {
        InitializeComponent();
        NameBox.Text = host.Name;
        IpAddressBox.Text = host.IpAddress;
        PortBox.Text = host.Port.ToString();
        EnabledBox.IsChecked = host.IsEnabled;
    }

    private async void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text, out var port) || port < 1 || port > 65535)
        {
            await ModalDialog.ShowMessageAsync(this, "入力エラー", "ポート番号は 1〜65535 の整数で入力してください。");
            return;
        }

        var ip = (IpAddressBox.Text ?? string.Empty).Trim();
        if (!OscHost.TryParseIpAddress(ip, out _))
        {
            await ModalDialog.ShowMessageAsync(this, "入力エラー",
                "IPアドレスを 192.168.1.10 の形式で入力してください。\nホスト名（例: qlab.local）は使えません。");
            return;
        }

        Close(new OscHost
        {
            Id = string.Empty, // caller will set
            Name = (NameBox.Text ?? string.Empty).Trim(),
            IpAddress = ip,
            Port = port,
            IsEnabled = EnabledBox.IsChecked ?? true,
        });
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
