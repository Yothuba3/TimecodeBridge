using System.Windows;
using TimecodeBridge.Models;
using TimecodeBridge.Services.Interfaces;
using TimecodeBridge.Views;

namespace TimecodeBridge.Services;

public class OscTriggerDialogService : IOscTriggerDialogService
{
    public OscTriggerEditResult ShowEditDialog(OscTriggerButton template, IReadOnlyList<OscHost> hosts, string title, bool canDelete)
    {
        var dialog = new OscTriggerButtonEditDialog(template, hosts, canDelete) { Title = title };
        if (Application.Current?.MainWindow is { } mainWindow)
            dialog.Owner = mainWindow;

        dialog.ShowDialog();

        return new OscTriggerEditResult(
            dialog.Action,
            dialog.Action == OscTriggerEditAction.Save ? dialog.ResultButton : null);
    }
}
