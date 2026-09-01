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

    event EventHandler<EventArgs> UnsavedChangesStatusChanged;
}
