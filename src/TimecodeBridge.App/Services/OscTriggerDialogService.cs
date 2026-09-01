using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.App.Views.Dialogs;

namespace TimecodeBridge.App.Services;

public class OscTriggerDialogService : IOscTriggerDialogService
{
    public OscTriggerEditResult ShowEditDialog(OscTriggerButton template, IReadOnlyList<OscHost> hosts, string title, bool canDelete)
    {
        return ModalDialog.Show(owner =>
            new OscTriggerButtonEditDialog(template, hosts, canDelete) { Title = title }
                .ShowDialog<OscTriggerEditResult>(owner));
    }
}
