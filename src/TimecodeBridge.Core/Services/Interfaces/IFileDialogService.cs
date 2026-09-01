namespace TimecodeBridge.Core.Services.Interfaces;

/// <summary>
/// ファイルダイアログ表示を担当するサービス
/// </summary>
public interface IFileDialogService
{
    string? ShowOpenFileDialog(string filter, string? initialDirectory = null);
    string? ShowSaveFileDialog(string filter, string? defaultFileName = null, string? initialDirectory = null);
}
