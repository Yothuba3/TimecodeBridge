using TimecodeBridge.Services.Interfaces;

namespace TimecodeBridge.macOS.Services;

/// <summary>
/// 最近使用したプロジェクトのMRUリスト管理サービス。
/// IAppSettingsServiceを経由して永続化を行う。
/// </summary>
public class RecentProjectsService : IRecentProjectsService
{
    private const int MaxRecentProjects = 10;

    private readonly IAppSettingsService _appSettings;
    private readonly List<string> _recentProjects;

    public RecentProjectsService(IAppSettingsService appSettings)
    {
        _appSettings = appSettings;
        _recentProjects = _appSettings.LoadRecentProjects();
    }

    public IReadOnlyList<string> GetRecentProjects()
    {
        return _recentProjects.AsReadOnly();
    }

    public void AddRecentProject(string filePath)
    {
        _recentProjects.Remove(filePath);
        _recentProjects.Insert(0, filePath);

        while (_recentProjects.Count > MaxRecentProjects)
        {
            _recentProjects.RemoveAt(_recentProjects.Count - 1);
        }

        try
        {
            _appSettings.SaveRecentProjects(_recentProjects);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"最近のプロジェクト一覧の永続化に失敗しました: {ex.Message}");
        }
    }
}
