using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
