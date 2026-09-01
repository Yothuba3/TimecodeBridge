using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using TimecodeBridge.Services.Interfaces;

namespace TimecodeBridge.Services;

/// <summary>
/// settings.json への設定永続化を担当するサービス
/// </summary>
public class AppSettingsService : IAppSettingsService
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _settingsFilePath;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="settingsFilePath">
    /// 設定ファイルパス。省略時は %APPDATA%/TimecodeBridge/settings.json を使用。
    /// テスト時に一時ファイルパスを渡すことで実ファイルを汚さない。
    /// </param>
    public AppSettingsService(string? settingsFilePath = null)
    {
        _settingsFilePath = settingsFilePath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
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
            // 既存設定を読み込み、RecentProjects のみ更新して保存
            var settings = LoadSettings() ?? new AppSettings();
            settings.RecentProjects = projects;
            SaveSettings(settings);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("最近のプロジェクト一覧の保存に失敗しました: {0}", ex.Message);
        }
    }

    /// <summary>
    /// 設定ファイルを読み込む。ファイルが存在しない場合や破損している場合は null を返す。
    /// </summary>
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

    /// <summary>
    /// 設定をファイルに書き込む
    /// </summary>
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

    /// <summary>
    /// settings.json のルート構造
    /// </summary>
    private class AppSettings
    {
        public List<string> RecentProjects { get; set; } = [];
    }
}
