using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using System.Windows;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Services.Interfaces;
using TimecodeBridge.Views;

namespace TimecodeBridge.Services;

public class CueDialogService : ICueDialogService
{
    public Cue? ShowEditDialog(Cue template, IReadOnlyList<OscHost> hosts, FrameRate frameRate, string title)
    {
        var dialog = new CueEditDialog(template, hosts, frameRate) { Title = title };
        if (Application.Current?.MainWindow is { } mainWindow)
            dialog.Owner = mainWindow;
        return dialog.ShowDialog() == true ? dialog.ResultCue : null;
    }

    public CueBatchEditResult? ShowBatchEditDialog(int cueCount, IReadOnlyList<OscHost> hosts, FrameRate frameRate)
    {
        var dialog = new CueBatchEditDialog(cueCount, hosts, frameRate);
        if (Application.Current?.MainWindow is { } mainWindow)
            dialog.Owner = mainWindow;
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public (int Count, TimeSpan Interval)? ShowBatchDuplicateDialog()
    {
        var dialog = new BatchDuplicateDialog();
        if (Application.Current?.MainWindow is { } mainWindow)
            dialog.Owner = mainWindow;
        if (dialog.ShowDialog() != true)
            return null;
        return (dialog.Count, dialog.Interval);
    }
}
