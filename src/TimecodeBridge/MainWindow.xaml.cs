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
    private const string FileFilter = MainViewModel.ProjectFileFilter;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closing += OnClosing;

        // 標準ファイル操作ショートカット（Ctrl+S / Ctrl+Shift+S / Ctrl+O / Ctrl+N）
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            System.Windows.Input.ApplicationCommands.Save,
            (_, _) => (DataContext as MainViewModel)?.SaveProjectCommand.Execute(null)));
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            System.Windows.Input.ApplicationCommands.SaveAs,
            (_, _) => SaveAsMenuItem_Click(this, new RoutedEventArgs())));
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            System.Windows.Input.ApplicationCommands.Open,
            (_, _) => OpenMenuItem_Click(this, new RoutedEventArgs())));
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            System.Windows.Input.ApplicationCommands.New,
            (_, _) => (DataContext as MainViewModel)?.NewProjectCommand.Execute(null)));

        // SaveAs には既定ジェスチャがないため明示的に割り当てる
        InputBindings.Add(new System.Windows.Input.KeyBinding(
            System.Windows.Input.ApplicationCommands.SaveAs,
            System.Windows.Input.Key.S,
            System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift));

        // ポン出し実行モードのEsc解除は、フォーカスがどこにあっても効くようウィンドウ全体で処理する
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape
                && OscTriggerPanel.DataContext is OscTriggerPanelViewModel { IsPlayMode: true } ponVm)
            {
                ponVm.IsEditMode = true;
                e.Handled = true;
            }
        };
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
            var cueListVm = App.Services.GetRequiredService<CueListViewModel>();
            CueList.DataContext = cueListVm;
            NextCueBar.DataContext = cueListVm;

            // Wire log panel
            var logViewModel = App.Services.GetRequiredService<LogViewModel>();
            LogListView.ItemsSource = logViewModel.Logs;
            ClearLogButton.Command = logViewModel.ClearLogsCommand;

            // 最新ログへ自動スクロール
            logViewModel.Logs.CollectionChanged += (_, _) =>
            {
                if (LogListView.Items.Count > 0)
                {
                    LogListView.ScrollIntoView(LogListView.Items[LogListView.Items.Count - 1]);
                }
            };

            // Wire HostManager and RelayControl views
            HostManager.DataContext = App.Services.GetRequiredService<HostManagerViewModel>();
            RelayControl.DataContext = App.Services.GetRequiredService<RelayViewModel>();

            // Wire OSC trigger panel
            OscTriggerPanel.DataContext = App.Services.GetRequiredService<OscTriggerPanelViewModel>();

            // Wire status bar (バッジごとに参照するViewModelが異なる)
            var timecodeVm = App.Services.GetRequiredService<TimecodeViewModel>();
            StatusSourceText.DataContext = timecodeVm;
            StatusMuteBadge.DataContext = timecodeVm;
            StatusRelayBadge.DataContext = App.Services.GetRequiredService<RelayViewModel>();
            StatusPlayModeBadge.DataContext = App.Services.GetRequiredService<OscTriggerPanelViewModel>();
        }
    }

    // ログの「失敗のみ」フィルタ切替
    private void FailOnlyCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (LogListView?.ItemsSource is null) return;

        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(LogListView.ItemsSource);
        view.Filter = FailOnlyCheck.IsChecked == true
            ? o => o is ViewModels.LogEntry entry && !entry.IsSuccess
            : null;
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
                // 保存先未確定なら保存ダイアログが出る。キャンセルされたら閉じるのも中止
                if (!mainViewModel.TrySaveWithPrompt())
                {
                    e.Cancel = true;
                }
                break;
            case MessageBoxResult.Cancel:
                e.Cancel = true;
                break;
            // MessageBoxResult.No => close without saving
        }
    }
}
