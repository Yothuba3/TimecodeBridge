using Avalonia.Platform.Storage;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.App.Services;

/// <summary>
/// macOS向けファイルダイアログサービス
/// Avalonia.Platform.Storage.IStorageProviderを使用してmacOS標準ダイアログを表示
/// </summary>
public class FileDialogService : IFileDialogService
{
    /// <summary>
    /// ファイルを開くダイアログを表示
    /// </summary>
    /// <param name="filter">ファイルフィルタ（例: "JSON Files|*.json"）</param>
    /// <param name="initialDirectory">初期ディレクトリ（オプション）</param>
    /// <returns>選択されたファイルパス（キャンセル時はnull）</returns>
    public string? ShowOpenFileDialog(string filter, string? initialDirectory = null)
    {
        return ModalDialog.Show(async owner =>
        {
            var storageProvider = owner.StorageProvider;

            var options = new FilePickerOpenOptions
            {
                Title = "ファイルを開く",
                AllowMultiple = false,
                FileTypeFilter = ParseFileFilter(filter)
            };

            // 初期ディレクトリの設定
            if (!string.IsNullOrEmpty(initialDirectory))
            {
                try
                {
                    var folder = await storageProvider.TryGetFolderFromPathAsync(initialDirectory);
                    if (folder != null)
                    {
                        options.SuggestedStartLocation = folder;
                    }
                }
                catch
                {
                    // 無効なパスの場合は無視
                }
            }

            var result = await storageProvider.OpenFilePickerAsync(options);
            return result.Count > 0 ? result[0].Path.LocalPath : null;
        });
    }

    /// <summary>
    /// ファイルを保存するダイアログを表示
    /// </summary>
    /// <param name="filter">ファイルフィルタ（例: "JSON Files|*.json"）</param>
    /// <param name="defaultFileName">デフォルトファイル名（オプション）</param>
    /// <param name="initialDirectory">初期ディレクトリ（オプション）</param>
    /// <returns>選択された保存先パス（キャンセル時はnull）</returns>
    public string? ShowSaveFileDialog(string filter, string? defaultFileName = null, string? initialDirectory = null)
    {
        return ModalDialog.Show(async owner =>
        {
            var storageProvider = owner.StorageProvider;

            var options = new FilePickerSaveOptions
            {
                Title = "ファイルを保存",
                SuggestedFileName = defaultFileName,
                FileTypeChoices = ParseFileFilter(filter)
            };

            // 初期ディレクトリの設定
            if (!string.IsNullOrEmpty(initialDirectory))
            {
                try
                {
                    var folder = await storageProvider.TryGetFolderFromPathAsync(initialDirectory);
                    if (folder != null)
                    {
                        options.SuggestedStartLocation = folder;
                    }
                }
                catch
                {
                    // 無効なパスの場合は無視
                }
            }

            var result = await storageProvider.SaveFilePickerAsync(options);
            return result?.Path.LocalPath;
        });
    }

    /// <summary>
    /// Windows形式のフィルタ文字列をAvalonia FilePickerFileTypeに変換
    /// </summary>
    /// <param name="filter">Windows形式のフィルタ（例: "JSON Files|*.json|All Files|*.*"）</param>
    /// <returns>FilePickerFileTypeのリスト</returns>
    private static List<FilePickerFileType>? ParseFileFilter(string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return null;

        var fileTypes = new List<FilePickerFileType>();

        // "JSON Files|*.json|All Files|*.*" 形式をパース
        var parts = filter.Split('|');
        for (int i = 0; i < parts.Length; i += 2)
        {
            if (i + 1 >= parts.Length)
                break;

            var name = parts[i];
            var patterns = parts[i + 1].Split(';').Select(p => p.Trim()).ToArray();

            // *.json -> .json に変換
            var extensions = patterns
                .Where(p => p.StartsWith("*.") && p != "*.*")
                .Select(p => p.Substring(1))
                .ToArray();

            // "All Files" (*.*)の場合は特別扱い
            if (patterns.Length == 1 && patterns[0] == "*.*")
            {
                fileTypes.Add(new FilePickerFileType("All Files")
                {
                    Patterns = new[] { "*.*" }
                });
            }
            else if (extensions.Length > 0)
            {
                fileTypes.Add(new FilePickerFileType(name)
                {
                    Patterns = patterns
                });
            }
        }

        return fileTypes.Count > 0 ? fileTypes : null;
    }
}
