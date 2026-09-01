using Avalonia.Controls;
using Avalonia.Interactivity;
using TimecodeBridge.App.ViewModels;

namespace TimecodeBridge.App.Views;

public partial class TimecodeDisplayView : UserControl
{
    public TimecodeDisplayView()
    {
        InitializeComponent();
    }

    // Cue-Sync送信先ホストのチェック変更をViewModelへ反映する
    private void OnCueSyncHostCheckChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TimecodeViewModel vm)
        {
            vm.CueSync.UpdateHostSelectionsCommand.Execute(null);
        }
    }
}
