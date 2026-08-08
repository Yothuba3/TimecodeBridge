using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TimecodeBridge.ViewModels;

namespace TimecodeBridge.Views;

public partial class OscTriggerPanelView : UserControl
{
    public OscTriggerPanelView()
    {
        InitializeComponent();
    }

    // ダブルクリックでセルを編集する（シングルクリックは Button.Click = 実行モードの送信）。
    private void OnCellPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;

        // 実行モードでは何もしない（e.Handled すると素早い連打の2打目が握りつぶされる）
        if (DataContext is not OscTriggerPanelViewModel vm || !vm.IsEditMode) return;

        if (sender is FrameworkElement { DataContext: OscTriggerCellViewModel cell })
        {
            vm.EditCellCommand.Execute(cell);
            e.Handled = true;
        }
    }

    // 行/列入力で Enter を押したら即時確定する（LostFocus を待たずにグリッドへ反映）。
    private void OnSizeBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox textBox)
        {
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
    }
}
