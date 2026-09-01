using Avalonia.Controls;
using TimecodeBridge.macOS.ViewModels;

namespace TimecodeBridge.macOS.Views;

/// <summary>
/// タイムコード表示ビュー
/// WPF MainWindow.xamlのタイムコード表示部分をAvalonia UIに移植
/// </summary>
public partial class TimecodeDisplayView : UserControl
{
    public TimecodeDisplayView()
    {
        InitializeComponent();
    }
}
