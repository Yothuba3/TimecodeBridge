using Avalonia.Controls;
using Avalonia.Interactivity;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.App.Services;
using TimecodeBridge.App.ViewModels;

namespace TimecodeBridge.App.Views.Dialogs;

/// <summary>
/// OSCポン出しボタン編集ダイアログ。結果は OscTriggerEditResult で返す。
/// </summary>
public partial class OscTriggerButtonEditDialog : Window
{
    private readonly OscTriggerButton _template;
    private readonly List<HostSelection> _hostItems;

    public OscTriggerButtonEditDialog() : this(
        new OscTriggerButton { Id = string.Empty }, [], canDelete: false)
    {
    }

    public OscTriggerButtonEditDialog(OscTriggerButton button, IReadOnlyList<OscHost> allHosts, bool canDelete)
    {
        InitializeComponent();
        _template = button;

        LabelBox.Text = button.Label;
        OscAddressBox.Text = button.OscAddress;
        OscArgsBox.Text = OscArgumentText.Format(button.Arguments);

        _hostItems = allHosts.Select(h => new HostSelection
        {
            Id = h.Id,
            Name = $"{h.Name} ({h.IpAddress}:{h.Port})",
            IsSelected = button.TargetHostIds.Contains(h.Id),
        }).ToList();
        HostList.ItemsSource = _hostItems;
        NoHostsHint.IsVisible = _hostItems.Count == 0;

        DeleteButton.IsVisible = canDelete;
    }

    private async void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var oscAddress = (OscAddressBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(oscAddress) || !oscAddress.StartsWith('/'))
        {
            await ModalDialog.ShowMessageAsync(this, "入力エラー", "OSCアドレスは '/' で始まる必要があります。");
            return;
        }

        // 不正な引数トークンを黙って捨てると「設定が消えた」ように見えるためエラーにする
        if (!OscArgumentText.TryParse(OscArgsBox.Text ?? string.Empty, out var arguments, out var invalidToken))
        {
            await ModalDialog.ShowMessageAsync(this, "入力エラー",
                $"OSC引数の形式が正しくありません: 「{invalidToken}」\n\n" +
                "「型:値」をスペース区切りで入力してください。\n例: i:1 f:0.5 s:hello（空白を含む文字列は s:\"hello world\"）");
            return;
        }

        var resultButton = new OscTriggerButton
        {
            Id = _template.Id,
            Row = _template.Row,
            Column = _template.Column,
            Label = (LabelBox.Text ?? string.Empty).Trim(),
            OscAddress = oscAddress,
            Arguments = arguments,
            TargetHostIds = _hostItems.Where(x => x.IsSelected).Select(x => x.Id).ToList(),
        };

        Close(new OscTriggerEditResult(OscTriggerEditAction.Save, resultButton));
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (!ModalDialog.Confirm("確認", "このボタンを削除しますか？", "削除")) return;

        Close(new OscTriggerEditResult(OscTriggerEditAction.Delete, null));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(new OscTriggerEditResult(OscTriggerEditAction.Cancel, null));
    }
}
