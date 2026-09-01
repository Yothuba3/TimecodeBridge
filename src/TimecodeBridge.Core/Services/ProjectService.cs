using System.IO;
using System.Text.Json;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Core.Services;

/// <summary>
/// プロジェクトファイルの読み書きのみを担当するサービス
/// </summary>
public class ProjectService : IProjectService
{
    private bool _hasUnsavedChanges;

    public string? CurrentFilePath { get; private set; }

    public bool HasUnsavedChanges => _hasUnsavedChanges;

    public event EventHandler<EventArgs>? UnsavedChangesStatusChanged;

    public ProjectData LoadProject(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Project file not found.", filePath);
        }

        var json = File.ReadAllText(filePath);
        var options = ProjectData.CreateJsonOptions();
        var data = JsonSerializer.Deserialize<ProjectData>(json, options)
            ?? throw new InvalidOperationException("Failed to deserialize project data.");

        CurrentFilePath = filePath;
        SetHasUnsavedChanges(false);

        return data;
    }

    public void SaveProject(string filePath, ProjectData data)
    {
        var options = ProjectData.CreateJsonOptions();
        var json = JsonSerializer.Serialize(data, options);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, json);

        CurrentFilePath = filePath;
        SetHasUnsavedChanges(false);
    }

    public void MarkAsChanged()
    {
        SetHasUnsavedChanges(true);
    }

    private void SetHasUnsavedChanges(bool value)
    {
        if (_hasUnsavedChanges == value) return;

        _hasUnsavedChanges = value;
        UnsavedChangesStatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
