using System;
using System.Collections.Specialized;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using TimecodeBridge.macOS.ViewModels;

namespace TimecodeBridge.macOS;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        DataContextChanged += OnDataContextChanged;

        // Auto-scroll to the latest log entry
        Loaded += (_, _) =>
        {
            if (LogListBox.ItemsSource is INotifyCollectionChanged collection)
            {
                collection.CollectionChanged += (_, _) =>
                {
                    if (LogListBox.ItemCount > 0)
                    {
                        LogListBox.ScrollIntoView(LogListBox.ItemCount - 1);
                    }
                };
            }
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // 波形ビューはMainViewModelを経由しないため、DIから直接取得して接続する
        if (DataContext is MainViewModel
            && Application.Current is App { Services: { } services })
        {
            AudioWaveform.DataContext = services.GetRequiredService<AudioWaveformViewModel>();
        }
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
            await viewModel.SaveProjectCommand.ExecuteAsync(null);
            Closing -= OnClosing;
            Close();
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
