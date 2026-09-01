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
    public event EventHandler<EventArgs>? ChangeCommitted;

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

        Normalize(data);

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
        ChangeCommitted?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        CurrentFilePath = null;
        SetHasUnsavedChanges(false);
    }

    /// <summary>手編集などでプロパティが明示的に null のファイルを既定値へ補正する</summary>
    private static void Normalize(ProjectData data)
    {
        data.Cues ??= [];
        data.Hosts ??= [];
        data.RelaySettings ??= new();
        data.SourceSettings ??= new();
        data.OscTriggerPanel ??= new();
        data.OscTriggerPanel.Buttons ??= [];
        data.RelaySettings.TargetHostIds ??= [];
        data.CueSync ??= new();
        data.CueSync.TargetHostIds ??= [];
        foreach (var cue in data.Cues.Where(c => c is not null))
        {
            cue.Arguments ??= [];
            cue.TargetHostIds ??= [];
            cue.AdditionalOscAddresses ??= [];

            // 旧形式の cueOffset（送信秒数の補正オフセット）は送信タイムコードへ変換して引き継ぐ
            if (cue.CueOffset is { } legacyOffset)
            {
                cue.SendTimecode ??= cue.TriggerTime.Add(legacyOffset);
                cue.CueOffset = null;
            }
        }
        data.Cues.RemoveAll(c => c is null || c.Id is null);
        data.Hosts.RemoveAll(h => h is null || h.Id is null);
        data.OscTriggerPanel.Buttons.RemoveAll(b => b is null || b.Id is null);
        foreach (var button in data.OscTriggerPanel.Buttons)
        {
            button.Arguments ??= [];
            button.TargetHostIds ??= [];
        }
    }

    private void SetHasUnsavedChanges(bool value)
    {
        if (_hasUnsavedChanges == value) return;

        _hasUnsavedChanges = value;
        UnsavedChangesStatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
