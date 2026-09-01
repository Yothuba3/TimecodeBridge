using Avalonia.Controls;
using Avalonia.Input;
using TimecodeBridge.macOS.ViewModels;

namespace TimecodeBridge.macOS.Views;

public partial class CueListView : UserControl
{
    public CueListView()
    {
        InitializeComponent();
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
