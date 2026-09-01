using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using System.Windows;

namespace TimecodeBridge.Views;

public partial class BatchDuplicateDialog : Window
{
    public int Count { get; private set; }

    /// <summary>複製ごとにトリガー時間へ加算する間隔（時:分:秒）。</summary>
    public TimeSpan Interval { get; private set; }

    public BatchDuplicateDialog()
    {
        InitializeComponent();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        // 上限は大量キュー一括生成によるUIフリーズ防止
        if (!int.TryParse(CountBox.Text, out var count) || count < 1 || count > 999)
        {
            MessageBox.Show("複製数は 1〜999 の整数を入力してください。", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryParseField(IntervalHoursBox.Text, 23, out var hours) ||
            !TryParseField(IntervalMinutesBox.Text, 59, out var minutes) ||
            !TryParseField(IntervalSecondsBox.Text, 59, out var seconds))
        {
            MessageBox.Show("間隔を正しい形式で入力してください。\n時(0-23) 分(0-59) 秒(0-59)", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var interval = new TimeSpan(hours, minutes, seconds);
        if (interval <= TimeSpan.Zero)
        {
            MessageBox.Show("間隔は1秒以上を指定してください。", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Count = count;
        Interval = interval;
        DialogResult = true;
    }

    // 空欄は0扱い、非数値・範囲外はエラー
    private static bool TryParseField(string text, int max, out int value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = 0;
            return true;
        }
        return int.TryParse(text, out value) && value >= 0 && value <= max;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
