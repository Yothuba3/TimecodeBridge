using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.macOS.Views.Dialogs;

namespace TimecodeBridge.macOS.Services;

public class CueDialogService : ICueDialogService
{
    public Cue? ShowEditDialog(Cue template, IReadOnlyList<OscHost> hosts, FrameRate frameRate, string title)
    {
        return ModalDialog.Show(owner =>
            new CueEditDialog(template, hosts, frameRate) { Title = title }.ShowDialog<Cue?>(owner));
    }

    public CueBatchEditResult? ShowBatchEditDialog(int cueCount, IReadOnlyList<OscHost> hosts, FrameRate frameRate)
    {
        return ModalDialog.Show(owner =>
            new CueBatchEditDialog(cueCount, hosts, frameRate).ShowDialog<CueBatchEditResult?>(owner));
    }

    public (int count, double intervalHours)? ShowBatchDuplicateDialog()
    {
        return ModalDialog.Show(owner =>
            new BatchDuplicateDialog().ShowDialog<(int count, double intervalHours)?>(owner));
    }
}
