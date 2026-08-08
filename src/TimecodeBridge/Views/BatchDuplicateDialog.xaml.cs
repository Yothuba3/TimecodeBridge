using System.Windows;

namespace TimecodeBridge.Views;

public partial class BatchDuplicateDialog : Window
{
    public int Count { get; private set; }
    public int IntervalHours { get; private set; }

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

        if (!int.TryParse(IntervalHoursBox.Text, out var intervalHours) || intervalHours < 1)
        {
            MessageBox.Show("間隔は1以上の整数を入力してください。", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Count = count;
        IntervalHours = intervalHours;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
