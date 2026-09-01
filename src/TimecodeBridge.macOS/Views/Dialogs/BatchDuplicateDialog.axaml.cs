using Avalonia.Controls;
using Avalonia.Interactivity;
using TimecodeBridge.macOS.Services;

namespace TimecodeBridge.macOS.Views.Dialogs;

/// <summary>
/// 連続複製ダイアログ。OKで (複製数, 間隔時間)、キャンセルで null を返す。
/// </summary>
public partial class BatchDuplicateDialog : Window
{
    public BatchDuplicateDialog()
    {
        InitializeComponent();
    }

    private async void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CountBox.Text, out var count) || count < 1)
        {
            await ModalDialog.ShowMessageAsync(this, "入力エラー", "複製数は1以上の整数を入力してください。");
            return;
        }

        if (!double.TryParse(IntervalHoursBox.Text, out var intervalHours) || intervalHours <= 0)
        {
            await ModalDialog.ShowMessageAsync(this, "入力エラー", "間隔は0より大きい数値を入力してください。");
            return;
        }

        Close(((int count, double intervalHours)?)(count, intervalHours));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
