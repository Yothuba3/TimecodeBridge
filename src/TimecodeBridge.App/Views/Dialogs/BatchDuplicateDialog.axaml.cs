using Avalonia.Controls;
using Avalonia.Interactivity;
using TimecodeBridge.App.Services;

namespace TimecodeBridge.App.Views.Dialogs;

/// <summary>
/// 連続複製ダイアログ。OKで (複製数, 間隔) 、キャンセルで null を返す。
/// </summary>
public partial class BatchDuplicateDialog : Window
{
    public BatchDuplicateDialog()
    {
        InitializeComponent();
    }

    private async void OnOkClick(object? sender, RoutedEventArgs e)
    {
        // 上限は大量キュー一括生成によるUIフリーズ防止
        if (!int.TryParse(CountBox.Text, out var count) || count < 1 || count > 999)
        {
            await ModalDialog.ShowMessageAsync(this, "入力エラー", "複製数は 1〜999 の整数を入力してください。");
            return;
        }

        if (!TryParseField(IntervalHoursBox.Text, 23, out var hours) ||
            !TryParseField(IntervalMinutesBox.Text, 59, out var minutes) ||
            !TryParseField(IntervalSecondsBox.Text, 59, out var seconds))
        {
            await ModalDialog.ShowMessageAsync(this, "入力エラー", "間隔を正しい形式で入力してください。\n時(0-23) 分(0-59) 秒(0-59)");
            return;
        }

        var interval = new TimeSpan(hours, minutes, seconds);
        if (interval <= TimeSpan.Zero)
        {
            await ModalDialog.ShowMessageAsync(this, "入力エラー", "間隔は1秒以上を指定してください。");
            return;
        }

        Close(((int Count, TimeSpan Interval)?)(count, interval));
    }

    // 空欄は0扱い、非数値・範囲外はエラー
    private static bool TryParseField(string? text, int max, out int value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = 0;
            return true;
        }
        return int.TryParse(text, out value) && value >= 0 && value <= max;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
