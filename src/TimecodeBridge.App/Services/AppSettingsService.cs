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
    /// 設定ファイルパス。省略時は macOS では ~/Library/Application Support/TimecodeBridge2/settings.json、
    /// Windows では %APPDATA%/TimecodeBridge2/settings.json を使用。
    /// テスト時に一時ファイルパスを渡すことで実ファイルを汚さない。
    /// </param>
    /// <param name="legacySettingsFilePath">
    /// 旧 TimecodeBridge の設定ファイルパス。省略時は旧アプリの既定の場所。
    /// 新しい設定ファイルがまだ無く旧ファイルがあれば、初回だけ内容を引き継ぐ。
    /// </param>
    public AppSettingsService(string? settingsFilePath = null, string? legacySettingsFilePath = null)
    {
        _settingsFilePath = settingsFilePath ?? DefaultSettingsPath(AppPaths.AppFolderName);
        ImportLegacySettings(legacySettingsFilePath ?? DefaultSettingsPath(AppPaths.LegacyAppFolderName));
    }

    private static string DefaultSettingsPath(string appFolderName)
        => Path.Combine(AppPaths.DataDirectoryFor(appFolderName), "settings.json");

    private void ImportLegacySettings(string legacySettingsFilePath)
    {
        try
        {
            if (File.Exists(_settingsFilePath) || !File.Exists(legacySettingsFilePath)) return;

            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.Copy(legacySettingsFilePath, _settingsFilePath);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("旧設定ファイルの取り込みに失敗しました: {0}", ex.Message);
        }
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
