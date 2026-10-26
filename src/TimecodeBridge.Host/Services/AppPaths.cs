namespace TimecodeBridge.Host.Services;

/// <summary>v3 の設定保存先。v2(TimecodeBridge2)と同じ Mac/PC に共存できるよう別フォルダにする。</summary>
public static class AppPaths
{
    public const string AppFolderName = "TimecodeBridge3";
    public const string LegacyAppFolderName = "TimecodeBridge2";

    public static string DataDirectoryFor(string appFolderName)
    {
        var root = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, appFolderName);
    }

    /// <summary>自動操作や試験用のインスタンスが利用者の設定(最近使ったプロジェクト)を書き換えないよう、環境変数で別の場所にできる。</summary>
    public const string DataDirectoryVariable = "TIMECODEBRIDGE_DATA_DIR";

    public static string DataDirectory =>
        Environment.GetEnvironmentVariable(DataDirectoryVariable) is { Length: > 0 } overridden ? overridden : DataDirectoryFor(AppFolderName);

    /// <summary>
    /// Windows の WebView2 のユーザーデータ(キャッシュなど)。既定の「実行ファイルの隣」は Program Files に入れると書けず表示できないので、
    /// ローミングしない LocalApplicationData の下に置く。TIMECODEBRIDGE_DATA_DIR があればその下。
    /// </summary>
    public static string WebView2UserDataDirectory =>
        Environment.GetEnvironmentVariable(DataDirectoryVariable) is { Length: > 0 } overridden
            ? Path.Combine(overridden, "WebView2")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName, "WebView2");
}
