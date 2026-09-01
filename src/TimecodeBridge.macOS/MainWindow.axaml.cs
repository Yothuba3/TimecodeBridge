using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using TimecodeBridge.macOS.ViewModels;

namespace TimecodeBridge.macOS;

public partial class MainWindow : Window
{
    private bool _failOnly;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        DataContextChanged += OnDataContextChanged;

        // ポン出し実行モードのEsc解除は、フォーカスがどこにあっても効くようウィンドウ全体で処理する
        AddHandler(KeyDownEvent, OnPreviewKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape
            && DataContext is MainViewModel { OscTriggerPanelViewModel.IsPlayMode: true } vm)
        {
            vm.OscTriggerPanelViewModel.IsEditMode = true;
            e.Handled = true;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // 波形ビューはMainViewModelを経由しないため、DIから直接取得して接続する
        if (Application.Current is App { Services: { } services })
        {
            AudioWaveform.DataContext = services.GetRequiredService<AudioWaveformViewModel>();
        }

        RefreshLogView(vm);
        vm.LogViewModel.Logs.CollectionChanged += (_, _) =>
        {
            RefreshLogView(vm);
            if (LogListBox.ItemCount > 0)
            {
                LogListBox.ScrollIntoView(LogListBox.ItemCount - 1);
            }
        };
    }

    // ログの「失敗のみ」フィルタ切替
    private void FailOnlyCheck_Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _failOnly = FailOnlyCheck.IsChecked == true;
        if (DataContext is MainViewModel vm) RefreshLogView(vm);
    }

    private void RefreshLogView(MainViewModel vm)
    {
        LogListBox.ItemsSource = _failOnly
            ? vm.LogViewModel.Logs.Where(l => !l.IsSuccess).ToList()
            : vm.LogViewModel.Logs;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        if (!viewModel.HasUnsavedChanges)
            return;

        e.Cancel = true;

        var result = await ShowUnsavedChangesDialog();

        if (result == UnsavedChangesDialogResult.Save)
        {
            // 保存先未確定なら保存ダイアログが出る。キャンセルされたら閉じるのも中止
            if (viewModel.TrySaveWithPrompt())
            {
                Closing -= OnClosing;
                Close();
            }
        }
        else if (result == UnsavedChangesDialogResult.DontSave)
        {
            Closing -= OnClosing;
            Close();
        }
    }

    private async Task<UnsavedChangesDialogResult> ShowUnsavedChangesDialog()
    {
        var dialog = new Window
        {
            Title = "確認",
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var result = UnsavedChangesDialogResult.Cancel;

        var stackPanel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 15
        };

        stackPanel.Children.Add(new TextBlock
        {
            Text = "プロジェクトに未保存の変更があります。保存しますか？",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 10
        };

        var saveButton = new Button { Content = "保存", Width = 90 };
        saveButton.Click += (_, _) => { result = UnsavedChangesDialogResult.Save; dialog.Close(); };

        var dontSaveButton = new Button { Content = "保存しない", Width = 90 };
        dontSaveButton.Click += (_, _) => { result = UnsavedChangesDialogResult.DontSave; dialog.Close(); };

        var cancelButton = new Button { Content = "キャンセル", Width = 90 };
        cancelButton.Click += (_, _) => { result = UnsavedChangesDialogResult.Cancel; dialog.Close(); };

        buttonPanel.Children.Add(saveButton);
        buttonPanel.Children.Add(dontSaveButton);
        buttonPanel.Children.Add(cancelButton);

        stackPanel.Children.Add(buttonPanel);
        dialog.Content = stackPanel;

        await dialog.ShowDialog(this);
        return result;
    }

    private enum UnsavedChangesDialogResult
    {
        Save,
        DontSave,
        Cancel
    }
}
