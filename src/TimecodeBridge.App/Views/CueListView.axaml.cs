using System.Globalization;
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

    /// <summary>
    /// 判定幅の入力を確定する。数値として読めない入力は <paramref name="current"/> を返す。
    /// 0 へ落とすとジャンプ判定が無効になり、頭出ししても途中のキューが発火してしまうため、
    /// 打ち間違いで設定が変わらないようにする。負数だけは 0 へ補正する。
    /// </summary>
    internal static int CommitTriggerWindowFrames(string? text, int current)
        => int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Max(0, value)
            : current;

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

        vm.TriggerWindowFrames = CommitTriggerWindowFrames(TriggerWindowBox.Text, vm.TriggerWindowFrames);
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
