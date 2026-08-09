using System.Windows;
using TimecodeBridge.Models;
using TimecodeBridge.Services.Interfaces;
using TimecodeBridge.ViewModels;

namespace TimecodeBridge.Views;

public partial class OscTriggerButtonEditDialog : Window
{
    private readonly OscTriggerButton _template;

    public OscTriggerButton ResultButton { get; private set; } = null!;
    public OscTriggerEditAction Action { get; private set; } = OscTriggerEditAction.Cancel;

    public OscTriggerButtonEditDialog(OscTriggerButton button, IReadOnlyList<OscHost> allHosts, bool canDelete)
    {
        InitializeComponent();
        _template = button;

        LabelBox.Text = button.Label;
        OscAddressBox.Text = button.OscAddress;
        OscArgsBox.Text = OscArgumentText.Format(button.Arguments);

        var hostItems = allHosts.Select(h => new HostSelection
        {
            Id = h.Id,
            Name = $"{h.Name} ({h.IpAddress}:{h.Port})",
            IsSelected = button.TargetHostIds.Contains(h.Id),
        }).ToList();
        HostListBox.ItemsSource = hostItems;
        NoHostsHint.Visibility = hostItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        DeleteButton.Visibility = canDelete ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        var oscAddress = OscAddressBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(oscAddress) || !oscAddress.StartsWith('/'))
        {
            MessageBox.Show("OSCアドレスは '/' で始まる必要があります。", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedHostIds = new List<string>();
        if (HostListBox.ItemsSource is IEnumerable<HostSelection> items)
        {
            selectedHostIds.AddRange(items.Where(x => x.IsSelected).Select(x => x.Id));
        }

        ResultButton = new OscTriggerButton
        {
            Id = _template.Id,
            Row = _template.Row,
            Column = _template.Column,
            Label = LabelBox.Text.Trim(),
            OscAddress = oscAddress,
            Arguments = OscArgumentText.Parse(OscArgsBox.Text),
            TargetHostIds = selectedHostIds,
        };

        Action = OscTriggerEditAction.Save;
        DialogResult = true;
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "このボタンを削除しますか？", "確認",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;

        Action = OscTriggerEditAction.Delete;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Action = OscTriggerEditAction.Cancel;
        DialogResult = false;
    }
}
