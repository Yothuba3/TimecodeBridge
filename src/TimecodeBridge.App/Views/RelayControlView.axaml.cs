using Avalonia.Controls;
using Avalonia.Interactivity;
using TimecodeBridge.App.ViewModels;

namespace TimecodeBridge.App.Views;

public partial class RelayControlView : UserControl
{
    public RelayControlView()
    {
        InitializeComponent();
    }

    private void OnHostCheckChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RelayViewModel vm)
        {
            vm.UpdateHostSelectionsCommand.Execute(null);
        }
    }
}
