using System.Windows;
using TimecodeBridge.Models;
using TimecodeBridge.Services.Interfaces;
using TimecodeBridge.Views;

namespace TimecodeBridge.Services;

public class OscTriggerDialogService : IOscTriggerDialogService
{
    public OscTriggerButton? ShowEditDialog(OscTriggerButton template, IReadOnlyList<OscHost> hosts, string title)
    {
        var dialog = new OscTriggerButtonEditDialog(template, hosts) { Title = title };
        if (Application.Current?.MainWindow is { } mainWindow)
            dialog.Owner = mainWindow;
        return dialog.ShowDialog() == true ? dialog.ResultButton : null;
    }
}
