using System.Windows;
using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Views;

public partial class HostEditDialog : Window
{
    public OscHost ResultHost { get; private set; } = null!;

    public HostEditDialog(OscHost host)
    {
        InitializeComponent();
        NameBox.Text = host.Name;
        IpAddressBox.Text = host.IpAddress;
        PortBox.Text = host.Port.ToString();
        EnabledBox.IsChecked = host.IsEnabled;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text, out var port) || port < 1 || port > 65535)
        {
            MessageBox.Show("ポート番号は 1〜65535 の整数で入力してください。", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var ip = IpAddressBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(ip))
        {
            MessageBox.Show("IPアドレスを入力してください。", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultHost = new OscHost
        {
            Id = string.Empty, // caller will set
            Name = NameBox.Text.Trim(),
            IpAddress = ip,
            Port = port,
            IsEnabled = EnabledBox.IsChecked ?? true,
        };

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
