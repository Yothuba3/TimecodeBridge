using System.Diagnostics;
using System.Text.Json;
using TimecodeBridge.Services.Interfaces;

namespace TimecodeBridge.App.Services;

/// <summary>
/// settings.json への設定永続化を担当するサービス(macOS版)
/// </summary>
public class AppSettingsService : IAppSettingsService
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _settingsFilePath;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="settingsFilePath">
    /// 設定ファイルパス。省略時は macOS では ~/Library/Application Support/TimecodeBridge/settings.json、
    /// Windows では %APPDATA%/TimecodeBridge/settings.json（旧WPF版と同じ場所）を使用。
    /// テスト時に一時ファイルパスを渡すことで実ファイルを汚さない。
    /// </param>
    public AppSettingsService(string? settingsFilePath = null)
    {
        _settingsFilePath = settingsFilePath ?? DefaultSettingsPath();
    }

    private static string DefaultSettingsPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TimecodeBridge",
                "settings.json");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Application Support",
            "TimecodeBridge",
            "settings.json");
    }

    public List<string> LoadRecentProjects()
    {
        var settings = LoadSettings();
        return settings?.RecentProjects ?? [];
    }

    public void SaveRecentProjects(List<string> projects)
    {
        try
        {
            var settings = LoadSettings() ?? new AppSettings();
            settings.RecentProjects = projects;
            SaveSettings(settings);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("最近のプロジェクト一覧の保存に失敗しました: {0}", ex.Message);
        }
    }

    private AppSettings? LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(json);
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("設定ファイルの読み込みに失敗しました: {0}", ex.Message);
        }

        return null;
    }

    private void SaveSettings(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, WriteOptions);
        File.WriteAllText(_settingsFilePath, json);
    }

    private class AppSettings
    {
        public List<string> RecentProjects { get; set; } = [];
    }
}
