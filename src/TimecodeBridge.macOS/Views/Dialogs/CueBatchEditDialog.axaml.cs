using Avalonia.Controls;
using Avalonia.Interactivity;
using TimecodeBridge.Core.Models;
using TimecodeBridge.macOS.Services;
using TimecodeBridge.macOS.ViewModels;

namespace TimecodeBridge.macOS.Views.Dialogs;

/// <summary>
/// キュー一括編集ダイアログ。適用で CueBatchEditResult、キャンセルで null を返す。
/// </summary>
public partial class CueBatchEditDialog : Window
{
    private readonly FrameRate _frameRate;
    private readonly List<HostSelection> _hostItems;

    public CueBatchEditDialog() : this(0, [], FrameRate.Fps30)
    {
    }

    public CueBatchEditDialog(int cueCount, IReadOnlyList<OscHost> allHosts, FrameRate frameRate)
    {
        InitializeComponent();
        _frameRate = frameRate;

        HeaderText.Text = $"{cueCount} 件のキューを一括編集";

        _hostItems = DialogInputs.ToHostSelections(allHosts, []);
        HostList.ItemsSource = _hostItems;
    }

    private async void OnOkClick(object? sender, RoutedEventArgs e)
    {
        bool anyChecked = ApplyOscAddress.IsChecked == true
                       || ApplySendTcSeconds.IsChecked == true
                       || ApplyOscArgs.IsChecked == true
                       || ApplyTargetHosts.IsChecked == true
                       || ApplyOffset.IsChecked == true
                       || ApplyMemo.IsChecked == true
                       || ApplyEnabled.IsChecked == true;

        if (!anyChecked)
        {
            await ModalDialog.ShowMessageAsync(this, "入力エラー", "変更するフィールドを1つ以上選択してください。");
            return;
        }

        var result = new CueBatchEditResult();

        if (ApplyOscAddress.IsChecked == true)
        {
            var oscAddress = (OscAddressBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(oscAddress) || !oscAddress.StartsWith('/'))
            {
                await ModalDialog.ShowMessageAsync(this, "入力エラー", "OSCアドレスは '/' で始まる必要があります。");
                return;
            }
            result.OscAddress = oscAddress;
        }

        if (ApplySendTcSeconds.IsChecked == true)
        {
            result.SendTriggerTimeAsSeconds = SendTcSecondsBox.IsChecked ?? false;
        }

        if (ApplyOscArgs.IsChecked == true)
        {
            result.Arguments = DialogInputs.ParseArguments(OscArgsBox.Text);
        }

        if (ApplyTargetHosts.IsChecked == true)
        {
            result.TargetHostIds = _hostItems.Where(x => x.IsSelected).Select(x => x.Id).ToList();
        }

        if (ApplyOffset.IsChecked == true)
        {
            result.ApplyOffset = true;
            result.CueOffset = DialogInputs.ParseOffset(
                OffsetSignBox.SelectedIndex == 1,
                OffsetHoursBox.Text, OffsetMinutesBox.Text, OffsetSecondsBox.Text, OffsetFramesBox.Text,
                _frameRate);
        }

        if (ApplyMemo.IsChecked == true)
        {
            result.ApplyMemo = true;
            result.Memo = MemoBox.Text ?? string.Empty;
        }

        if (ApplyEnabled.IsChecked == true)
        {
            result.IsEnabled = EnabledBox.IsChecked ?? true;
        }

        Close(result);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
