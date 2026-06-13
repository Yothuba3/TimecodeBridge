using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using TimecodeBridge.ViewModels;

namespace TimecodeBridge;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const string FileFilter = "TimecodeBridge プロジェクト (*.json)|*.json|すべてのファイル (*.*)|*.*";

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closing += OnClosing;
    }

    private void OpenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = FileFilter,
            Title = "プロジェクトを開く",
        };

        if (dialog.ShowDialog() == true && DataContext is MainViewModel vm)
        {
            vm.OpenProjectCommand.Execute(dialog.FileName);
        }
    }

    private void SaveAsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = FileFilter,
            Title = "名前を付けて保存",
            DefaultExt = ".json",
        };

        if (dialog.ShowDialog() == true && DataContext is MainViewModel vm)
        {
            vm.SaveProjectAsCommand.Execute(dialog.FileName);
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is MainViewModel mainVm)
        {
            // Wire child views to their ViewModels via DI
            TimecodeDisplay.DataContext = App.Services.GetRequiredService<TimecodeViewModel>();
            AudioWaveform.DataContext = App.Services.GetRequiredService<AudioWaveformViewModel>();
            CueList.DataContext = App.Services.GetRequiredService<CueListViewModel>();

            // Wire log panel
            var logViewModel = App.Services.GetRequiredService<LogViewModel>();
            LogListView.ItemsSource = logViewModel.Logs;
            ClearLogButton.Command = logViewModel.ClearLogsCommand;

            // Wire HostManager and RelayControl views
            HostManager.DataContext = App.Services.GetRequiredService<HostManagerViewModel>();
            RelayControl.DataContext = App.Services.GetRequiredService<RelayViewModel>();

            // Wire OSC trigger panel
            OscTriggerPanel.DataContext = App.Services.GetRequiredService<OscTriggerPanelViewModel>();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is not MainViewModel mainViewModel)
            return;

        if (!mainViewModel.HasUnsavedChanges)
            return;

        var result = MessageBox.Show(
            "プロジェクトに未保存の変更があります。保存しますか？",
            "確認",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        switch (result)
        {
            case MessageBoxResult.Yes:
                mainViewModel.SaveProjectCommand.Execute(null);
                break;
            case MessageBoxResult.Cancel:
                e.Cancel = true;
                break;
            // MessageBoxResult.No => close without saving
        }
    }
}
