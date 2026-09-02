using System.Runtime.InteropServices;

namespace TimecodeBridge.App.Services;

/// <summary>
/// 起動失敗や未処理例外の内容をファイルへ残す。
/// GUIアプリには標準エラー出力が無く、落ちた理由を後から確認する手段がこれしかない。
/// </summary>
public static class CrashLog
{
    public const string FileName = "startup-error.log";

    public static string DefaultPath => Path.Combine(AppPaths.DataDirectory, FileName);

    /// <summary>
    /// ログを追記し、書けたファイルのパスを返す。書けなかった場合は null。
    /// 落ちている最中に呼ばれるので、ここからは決して例外を出さない。
    /// </summary>
    public static string? Write(string stage, Exception exception, string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var version = typeof(CrashLog).Assembly.GetName().Version?.ToString() ?? "unknown";
            var entry =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] stage={stage} version={version} os={RuntimeInformation.OSDescription}" +
                $"{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(path, entry);
            return path;
        }
        catch
        {
            return null;
        }
    }
}
