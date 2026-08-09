using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TimecodeBridge.ViewModels;

namespace TimecodeBridge.Views;

/// <summary>
/// Interaction logic for CueListView.xaml
/// </summary>
public partial class CueListView : UserControl
{
    public CueListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is CueListViewModel oldVm)
        {
            oldVm.NextCueChanged -= OnNextCueChanged;
        }
        if (e.NewValue is CueListViewModel newVm)
        {
            newVm.NextCueChanged += OnNextCueChanged;
        }
    }

    // 次キューが画面外でもオペレーターに見えるよう表示位置を追従させる
    private void OnNextCueChanged(object? sender, CueItemViewModel item)
    {
        CueDataGrid.ScrollIntoView(item);
    }

    private void CueItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 手動トリガーボタンや有効チェックの連打を編集ダイアログとして誤認しない
        if (IsOnInteractiveChild(e.OriginalSource as DependencyObject, sender as DependencyObject)) return;

        if (sender is ListViewItem item
            && item.DataContext is CueItemViewModel cueItem
            && DataContext is CueListViewModel viewModel)
        {
            viewModel.EditCueCommand.Execute(cueItem.Id);
            e.Handled = true;
        }
    }

    // origin から root まで遡り、途中にボタン系コントロールがあれば true
    private static bool IsOnInteractiveChild(DependencyObject? origin, DependencyObject? root)
    {
        var current = origin;
        while (current is not null && current != root)
        {
            if (current is ButtonBase) return true;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }
}
