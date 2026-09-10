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

    public static string DataDirectory => DataDirectoryFor(AppFolderName);
}
