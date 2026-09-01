using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TimecodeBridge.App.ViewModels;

namespace TimecodeBridge.App.Views;

public partial class OscTriggerPanelView : UserControl
{
    private OscTriggerPanelViewModel? _vm;

    public OscTriggerPanelView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = DataContext as OscTriggerPanelViewModel;
            if (_vm is not null) _vm.PropertyChanged += OnVmPropertyChanged;
            ApplyGridSize();
        };
        CellsControl.LayoutUpdated += (_, _) => ApplyGridSize();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OscTriggerPanelViewModel.Rows) or nameof(OscTriggerPanelViewModel.Columns))
        {
            ApplyGridSize();
        }
    }

    // 行/列入力の確定。intバインディング直結だと空文字で変換エラーになるため、
    // ここで検証して確定する（数値以外・1未満は1へ補正、Enterで即時反映）
    private void OnSizeBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return && sender is TextBox box)
        {
            CommitSizeBox(box);
            e.Handled = true;
        }
    }

    private void OnSizeBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox box) CommitSizeBox(box);
    }

    private void CommitSizeBox(TextBox box)
    {
        if (_vm is null) return;

        if (!int.TryParse(box.Text, out var value) || value < 1) value = 1;

        if (ReferenceEquals(box, RowsBox)) _vm.Rows = value;
        else _vm.Columns = value;

        // ViewModel側クランプ（上限32）や縮小キャンセル後の実値を表示へ戻す
        box.Text = (ReferenceEquals(box, RowsBox) ? _vm.Rows : _vm.Columns).ToString();
    }

    // ItemsPanel内のUniformGridはDataContext連鎖に乗らないため、コードで行列数を反映する
    private void ApplyGridSize()
    {
        if (_vm is null) return;
        var grid = CellsControl.GetVisualDescendants().OfType<UniformGrid>().FirstOrDefault();
        if (grid is null) return;
        if (grid.Rows != _vm.Rows) grid.Rows = _vm.Rows;
        if (grid.Columns != _vm.Columns) grid.Columns = _vm.Columns;
    }
}
