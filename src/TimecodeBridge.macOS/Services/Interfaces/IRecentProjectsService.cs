namespace TimecodeBridge.Services.Interfaces;

/// <summary>
/// 最近使用したプロジェクトのMRUリスト管理サービス
/// </summary>
public interface IRecentProjectsService
{
    /// <summary>
    /// 最近使用したプロジェクトの一覧を取得する
    /// </summary>
    IReadOnlyList<string> GetRecentProjects();

    /// <summary>
    /// 最近使用したプロジェクトに追加する（MRU順に更新）
    /// </summary>
    void AddRecentProject(string filePath);
}
