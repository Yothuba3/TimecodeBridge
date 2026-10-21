using System.Globalization;
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

    /// <summary>
    /// 行/列の入力を確定する。数値として読めない入力と 1 未満は <paramref name="current"/> を返す。
    /// 1 へ落とすとグリッドが潰れ、範囲外になったボタンの削除確認まで出てしまうため、
    /// 打ち間違いで形が変わらないようにする。
    /// </summary>
    internal static int CommitGridSize(string? text, int current)
        => int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 1
            ? value
            : current;

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

        var isRows = ReferenceEquals(box, RowsBox);
        var value = CommitGridSize(box.Text, isRows ? _vm.Rows : _vm.Columns);

        if (isRows) _vm.Rows = value;
        else _vm.Columns = value;

        // ViewModel側クランプ（上限32）や縮小キャンセル後の実値を表示へ戻す
        box.Text = (isRows ? _vm.Rows : _vm.Columns).ToString();
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
