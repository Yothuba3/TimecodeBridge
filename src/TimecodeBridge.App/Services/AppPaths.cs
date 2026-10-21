namespace TimecodeBridge.App.Services;

/// <summary>ユーザーごとのアプリデータ置き場（設定ファイル・エラーログ）。</summary>
public static class AppPaths
{
    public const string AppFolderName = "TimecodeBridge2";
    public const string LegacyAppFolderName = "TimecodeBridge";

    /// <summary>試験用インスタンスが利用者の設定を書き換えないよう、保存先の親ディレクトリを差し替える環境変数。</summary>
    public const string OverrideVariable = "TIMECODEBRIDGE_DATA_DIR";

    public static string DataDirectory => DataDirectoryFor(AppFolderName);

    /// <summary>
    /// Windows は %APPDATA% 配下、それ以外は ~/Library/Application Support 配下。
    /// 環境変数 TIMECODEBRIDGE_DATA_DIR があればその配下(旧アプリの設定も同じ親の下を見るので、実ファイルは取り込まない)。
    /// </summary>
    public static string DataDirectoryFor(string folderName)
    {
        var overridden = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return Path.Combine(overridden, folderName);
        }

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
