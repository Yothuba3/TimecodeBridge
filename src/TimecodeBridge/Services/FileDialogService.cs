using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using Microsoft.Win32;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Services;

public class FileDialogService : IFileDialogService
{
    public string? ShowOpenFileDialog(string filter, string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog { Filter = filter };
        if (initialDirectory is not null)
            dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ShowSaveFileDialog(string filter, string? defaultFileName = null, string? initialDirectory = null)
    {
        var dialog = new SaveFileDialog { Filter = filter };
        if (defaultFileName is not null)
            dialog.FileName = defaultFileName;
        if (initialDirectory is not null)
            dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
