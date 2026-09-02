namespace TimecodeBridge.App.Services;

/// <summary>ユーザーごとのアプリデータ置き場（設定ファイル・エラーログ）。</summary>
public static class AppPaths
{
    public const string AppFolderName = "TimecodeBridge2";
    public const string LegacyAppFolderName = "TimecodeBridge";

    public static string DataDirectory => DataDirectoryFor(AppFolderName);

    /// <summary>Windows は %APPDATA% 配下、それ以外は ~/Library/Application Support 配下。</summary>
    public static string DataDirectoryFor(string folderName)
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                folderName);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Application Support",
            folderName);
    }
}
