using System.Windows;
using System.Windows.Controls;
using TimecodeBridge.ViewModels;

namespace TimecodeBridge.Views;

/// <summary>
/// Interaction logic for TimecodeDisplayView.xaml
/// </summary>
public partial class TimecodeDisplayView : UserControl
{
    public TimecodeDisplayView()
    {
        InitializeComponent();
    }

    // Cue-Sync送信先ホストのチェック変更をViewModelへ反映する
    private void OnCueSyncHostCheckChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is TimecodeViewModel vm)
        {
            vm.CueSync.UpdateHostSelectionsCommand.Execute(null);
        }
    }
}
