using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.App.Views.Dialogs;

namespace TimecodeBridge.App.Services;

public class HostDialogService : IHostDialogService
{
    public OscHost? ShowEditDialog(OscHost template)
    {
        return ModalDialog.Show(owner => new HostEditDialog(template).ShowDialog<OscHost?>(owner));
    }
}
