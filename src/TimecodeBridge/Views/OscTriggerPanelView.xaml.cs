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

        if (sender is FrameworkElement { DataContext: OscTriggerCellViewModel cell }
            && DataContext is OscTriggerPanelViewModel vm
            && vm.EditCellCommand.CanExecute(cell))
        {
            vm.EditCellCommand.Execute(cell);
            e.Handled = true;
        }
    }
}
