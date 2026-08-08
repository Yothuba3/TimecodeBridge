using TimecodeBridge.Models;

namespace TimecodeBridge.Services.Interfaces;

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
}
