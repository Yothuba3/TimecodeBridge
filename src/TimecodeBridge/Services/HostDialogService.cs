using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using System.Windows;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Services.Interfaces;
using TimecodeBridge.Views;

namespace TimecodeBridge.Services;

public class HostDialogService : IHostDialogService
{
    public OscHost? ShowEditDialog(OscHost template)
    {
        var dialog = new HostEditDialog(template);
        if (Application.Current?.MainWindow is { } mainWindow)
            dialog.Owner = mainWindow;
        return dialog.ShowDialog() == true ? dialog.ResultHost : null;
    }
}
