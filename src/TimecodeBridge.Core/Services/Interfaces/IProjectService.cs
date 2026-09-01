using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Core.Services.Interfaces;

/// <summary>
/// プロジェクトファイルの読み書きのみを担当するサービス
/// </summary>
public interface IProjectService
{
    string? CurrentFilePath { get; }
    bool HasUnsavedChanges { get; }

    ProjectData LoadProject(string filePath);
    void SaveProject(string filePath, ProjectData data);
    void MarkAsChanged();

    /// <summary>新規プロジェクト状態へ戻す（保存先パスと未保存フラグをクリア）</summary>
    void Reset();

    event EventHandler<EventArgs> UnsavedChangesStatusChanged;

    /// <summary>
    /// 編集操作が確定するたびに発火する（Undo履歴の記録用）。
    /// <see cref="UnsavedChangesStatusChanged"/> と異なり、dirty状態の遷移に関係なく毎回発火する。
    /// </summary>
    event EventHandler<EventArgs> ChangeCommitted;
}
