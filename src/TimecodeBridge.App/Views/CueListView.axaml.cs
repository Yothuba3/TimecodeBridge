using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TimecodeBridge.App.ViewModels;

namespace TimecodeBridge.App.Views;

public partial class CueListView : UserControl
{
    public CueListView()
    {
        InitializeComponent();
    }

    // 判定幅の確定。intバインディング直結だと空文字で変換エラーになるため、
    // ここで検証して確定する（数値以外・負数は0へ補正、Enterで即時反映）
    private void OnTriggerWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            CommitTriggerWindow();
            e.Handled = true;
        }
    }

    private void OnTriggerWindowLostFocus(object? sender, RoutedEventArgs e)
    {
        CommitTriggerWindow();
    }

    private void CommitTriggerWindow()
    {
        if (DataContext is not CueListViewModel vm) return;

        if (!int.TryParse(TriggerWindowBox.Text, out var value) || value < 0) value = 0;
        vm.TriggerWindowFrames = value;
        TriggerWindowBox.Text = vm.TriggerWindowFrames.ToString();
    }

    private void CueList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (CueList.SelectedItem is CueItemViewModel cueItem
            && DataContext is CueListViewModel viewModel)
        {
            viewModel.EditCueCommand.Execute(cueItem.Id);
            e.Handled = true;
        }
    }
}
