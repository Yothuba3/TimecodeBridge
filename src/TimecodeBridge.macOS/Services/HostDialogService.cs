using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.macOS.Views.Dialogs;

namespace TimecodeBridge.macOS.Services;

public class HostDialogService : IHostDialogService
{
    public OscHost? ShowEditDialog(OscHost template)
    {
        return ModalDialog.Show(owner => new HostEditDialog(template).ShowDialog<OscHost?>(owner));
    }
}
