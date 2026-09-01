using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
namespace TimecodeBridge.Services.Interfaces;

/// <summary>
/// アプリケーション設定の永続化サービス
/// </summary>
public interface IAppSettingsService
{
    /// <summary>
    /// 最近使用したプロジェクトの一覧を読み込む
    /// </summary>
    List<string> LoadRecentProjects();

    /// <summary>
    /// 最近使用したプロジェクトの一覧を保存する
    /// </summary>
    void SaveRecentProjects(List<string> projects);
}
