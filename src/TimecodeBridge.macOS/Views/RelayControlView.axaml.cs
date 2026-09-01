using Avalonia.Controls;
using Avalonia.Interactivity;
using TimecodeBridge.macOS.ViewModels;

namespace TimecodeBridge.macOS.Views;

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
