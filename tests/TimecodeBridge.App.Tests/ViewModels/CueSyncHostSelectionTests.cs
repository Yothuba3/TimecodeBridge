using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.App.ViewModels;

namespace TimecodeBridge.App.Tests.ViewModels;

/// <summary>CueSyncの送信先ホスト全選択/全解除</summary>
public class CueSyncHostSelectionTests
{
    private sealed class StubHostRegistry : IHostRegistry
    {
        public List<OscHost> Items { get; } =
        [
            new() { Id = "h1", Name = "A", IpAddress = "1.1.1.1", Port = 1 },
            new() { Id = "h2", Name = "B", IpAddress = "2.2.2.2", Port = 2 },
        ];
        public IReadOnlyList<OscHost> Hosts => Items;
        public event EventHandler<HostChangedEventArgs>? HostChanged;
        public void AddHost(OscHost host) { }
        public void UpdateHost(string hostId, OscHost updatedHost) { }
        public void RemoveHost(string hostId) { }
        public void SetHostEnabled(string hostId, bool enabled) { }
        public IReadOnlyList<OscHost> GetEnabledHosts(IReadOnlyList<string> hostIds) => [];
    }

    private sealed class StubCueManager : ICueManager
    {
        public IReadOnlyList<Cue> Cues => [];
        public int TriggerWindowFrames { get; set; }
        public bool IsMuted { get; set; }
        public bool IsAutoMuteEnabled { get; set; } = true;
        public string? AutoMutedCueId => null;
        public DateTime? AutoUnmuteAt => null;
        public event EventHandler<CueTriggeredEventArgs>? CueTriggered;
        public event EventHandler? MuteStateChanged;
        public void AddCue(Cue cue) { }
        public void UpdateCue(string cueId, Cue updatedCue) { }
        public void RemoveCue(string cueId) { }
        public void ReorderCues(IReadOnlyList<string> orderedCueIds) { }
        public void SetCueEnabled(string cueId, bool enabled) { }
        public void ManualTrigger(string cueId) { }
        public void ResetTracking() { }
        public void SendCueSync(string oscAddress, IReadOnlyList<string> targetHostIds) { }
    }

    private sealed class StubProjectService : IProjectService
    {
        public string? CurrentFilePath => null;
        public bool HasUnsavedChanges => false;
        public event EventHandler<EventArgs>? UnsavedChangesStatusChanged;
        public event EventHandler<EventArgs>? ChangeCommitted;
        public ProjectData LoadProject(string filePath) => new();
        public void SaveProject(string filePath, ProjectData data) { }
        public void MarkAsChanged() { }
        public void Reset() { }
    }

    [Fact]
    public void 全選択で全ホストが送信先になり全解除で空になる()
    {
        var vm = new CueSyncViewModel(new StubCueManager(), new StubHostRegistry(), new StubProjectService());

        vm.SelectAllHostsCommand.Execute(null);
        Assert.Equal(["h1", "h2"], vm.TargetHostIds.Order());
        Assert.All(vm.HostSelections, h => Assert.True(h.IsSelected));

        vm.ClearAllHostsCommand.Execute(null);
        Assert.Empty(vm.TargetHostIds);
        Assert.All(vm.HostSelections, h => Assert.False(h.IsSelected));
    }
}
