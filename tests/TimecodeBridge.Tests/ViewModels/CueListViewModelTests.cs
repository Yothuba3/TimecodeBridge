namespace TimecodeBridge.Tests.ViewModels;

using TimecodeBridge.Models;
using TimecodeBridge.Services;
using TimecodeBridge.Services.Interfaces;
using TimecodeBridge.ViewModels;

public class CueListViewModelTests
{
    private static TimecodeValue TC(int h, int m, int s, int f) =>
        new(h, m, s, f, FrameRate.Fps30);

    private static Cue CreateCue(string id = "cue-1", string name = "Test Cue",
        string oscAddress = "/test", int triggerSeconds = 10, bool enabled = true)
    {
        return new Cue
        {
            Id = id,
            Name = name,
            TriggerTime = TC(0, 0, triggerSeconds, 0),
            OscAddress = oscAddress,
            IsEnabled = enabled,
        };
    }

    // --- Construction ---

    private static CueListViewModel CreateVm(StubCueManager cueManager, StubTimecodeEngine engine)
    {
        var stubCueDialogService = new StubCueDialogService();
        var vm = new CueListViewModel(cueManager, engine, new StubHostRegistry(), stubCueDialogService, new StubProjectService());
        return vm;
    }

    [Fact]
    public void Constructor_InitializesEmptyCueItems()
    {
        var cueManager = new StubCueManager();
        var engine = new StubTimecodeEngine();
        var vm = CreateVm(cueManager, engine);

        Assert.NotNull(vm.CueItems);
        Assert.Empty(vm.CueItems);
    }

    [Fact]
    public void Constructor_WithExistingCues_PopulatesCueItems()
    {
        var cueManager = new StubCueManager();
        cueManager.AddCue(CreateCue("c1", "Cue 1"));
        cueManager.AddCue(CreateCue("c2", "Cue 2"));

        var engine = new StubTimecodeEngine();
        var vm = CreateVm(cueManager, engine);

        Assert.Equal(2, vm.CueItems.Count);
        Assert.Equal("c1", vm.CueItems[0].Id);
        Assert.Equal("c2", vm.CueItems[1].Id);
    }

    // --- AddCueCommand ---

    [Fact]
    public void AddCueCommand_AddsNewCueToCueManager()
    {
        var cueManager = new StubCueManager();
        var engine = new StubTimecodeEngine();
        var vm = CreateVm(cueManager, engine);

        vm.AddCueCommand.Execute(null);

        Assert.Single(cueManager.Cues);
        Assert.Single(vm.CueItems);
    }

    [Fact]
    public void AddCueCommand_MultipleCalls_AddsMultipleCues()
    {
        var cueManager = new StubCueManager();
        var engine = new StubTimecodeEngine();
        var vm = CreateVm(cueManager, engine);

        vm.AddCueCommand.Execute(null);
        vm.AddCueCommand.Execute(null);

        Assert.Equal(2, cueManager.Cues.Count);
        Assert.Equal(2, vm.CueItems.Count);
    }

    // --- RemoveCueCommand ---

    [Fact]
    public void RemoveCueCommand_RemovesCueFromManagerAndItems()
    {
        var cueManager = new StubCueManager();
        cueManager.AddCue(CreateCue("c1", "Cue 1"));
        cueManager.AddCue(CreateCue("c2", "Cue 2"));
        var engine = new StubTimecodeEngine();
        var vm = CreateVm(cueManager, engine);

        vm.RemoveCueCommand.Execute("c1");

        Assert.Single(cueManager.Cues);
        Assert.Equal("c2", cueManager.Cues[0].Id);
        Assert.Single(vm.CueItems);
        Assert.Equal("c2", vm.CueItems[0].Id);
    }

    // --- ManualTriggerCommand ---

    [Fact]
    public void ManualTriggerCommand_CallsCueManagerManualTrigger()
    {
        var cueManager = new StubCueManager();
        cueManager.AddCue(CreateCue("c1"));
        var engine = new StubTimecodeEngine();
        var vm = CreateVm(cueManager, engine);

        vm.ManualTriggerCommand.Execute("c1");

        Assert.Contains("c1", cueManager.ManualTriggerCalls);
    }

    // --- ToggleCueEnabledCommand ---

    // CheckBoxのTwoWayバインディングが先に item.IsEnabled を反転し、
    // コマンドはその値をManagerへ同期する契約（二重反転バグ修正後の仕様）

    [Fact]
    public void ToggleCueEnabledCommand_SyncsDisabledStateToManager()
    {
        var cueManager = new StubCueManager();
        cueManager.AddCue(CreateCue("c1", enabled: true));
        var engine = new StubTimecodeEngine();
        var vm = CreateVm(cueManager, engine);

        vm.CueItems[0].IsEnabled = false; // バインディングによる反転を模倣
        vm.ToggleCueEnabledCommand.Execute("c1");

        Assert.Single(cueManager.SetCueEnabledCalls);
        Assert.Equal("c1", cueManager.SetCueEnabledCalls[0].CueId);
        Assert.False(cueManager.SetCueEnabledCalls[0].Enabled);
    }

    [Fact]
    public void ToggleCueEnabledCommand_SyncsEnabledStateToManager()
    {
        var cueManager = new StubCueManager();
        cueManager.AddCue(CreateCue("c1", enabled: false));
        var engine = new StubTimecodeEngine();
        var vm = CreateVm(cueManager, engine);

        vm.CueItems[0].IsEnabled = true; // バインディングによる反転を模倣
        vm.ToggleCueEnabledCommand.Execute("c1");

        Assert.Single(cueManager.SetCueEnabledCalls);
        Assert.Equal("c1", cueManager.SetCueEnabledCalls[0].CueId);
        Assert.True(cueManager.SetCueEnabledCalls[0].Enabled);
    }

    [Fact]
    public void ToggleCueEnabledCommand_DoesNotRevertCueItemEnabled()
    {
        var cueManager = new StubCueManager();
        cueManager.AddCue(CreateCue("c1", enabled: true));
        var engine = new StubTimecodeEngine();
        var vm = CreateVm(cueManager, engine);

        vm.CueItems[0].IsEnabled = false; // バインディングによる反転を模倣
        vm.ToggleCueEnabledCommand.Execute("c1");

        // コマンドが再反転して元に戻さないこと
        Assert.False(vm.CueItems[0].IsEnabled);
    }

    // --- BatchDuplicateCueCommand（分・秒単位の間隔） ---

    [Fact]
    public void BatchDuplicateCue_MinuteSecondInterval_AddsAtEachStep()
    {
        var cueManager = new StubCueManager();
        cueManager.AddCue(CreateCue("c1", "Base", "/base", 10)); // 00:00:10:00
        var engine = new StubTimecodeEngine();
        var vm = new CueListViewModel(cueManager, engine, new StubHostRegistry(),
            new BatchDuplicateStubDialogService(2, new TimeSpan(0, 1, 30)), new StubProjectService());

        vm.BatchDuplicateCueCommand.Execute("c1");

        Assert.Equal(3, cueManager.Cues.Count);
        // +1分30秒 ずつ
        Assert.Equal(TC(0, 1, 40, 0), cueManager.Cues[1].TriggerTime);
        Assert.Equal(TC(0, 3, 10, 0), cueManager.Cues[2].TriggerTime);
    }

    // --- CueTriggered event ---

    [StaFact]
    public void CueTriggered_SetsIsTriggeredOnMatchingCueItem()
    {
        var cueManager = new StubCueManager();
        var cue = CreateCue("c1");
        cueManager.AddCue(cue);
        var engine = new StubTimecodeEngine();
        var vm = CreateVm(cueManager, engine);

        cueManager.SimulateCueTriggered(cue, TC(0, 0, 10, 0), false);

        Assert.True(vm.CueItems[0].IsTriggered);
    }

    // --- TimecodeUpdated: IsNextCue ---

    [StaFact]
    public void TimecodeUpdated_SetsIsNextCueOnNextTriggerableCue()
    {
        var cueManager = new StubCueManager();
        cueManager.AddCue(CreateCue("c1", triggerSeconds: 5));
        cueManager.AddCue(CreateCue("c2", triggerSeconds: 10));
        cueManager.AddCue(CreateCue("c3", triggerSeconds: 15));
        var engine = new StubTimecodeEngine();
        var vm = CreateVm(cueManager, engine);

        // Current timecode is at 7s -> next cue should be c2 (10s)
        engine.SimulateTimecodeUpdate(TC(0, 0, 7, 0), TC(0, 0, 7, 0));

        Assert.False(vm.CueItems[0].IsNextCue); // c1 at 5s - already passed
        Assert.True(vm.CueItems[1].IsNextCue);   // c2 at 10s - next
        Assert.False(vm.CueItems[2].IsNextCue); // c3 at 15s - not next
    }

    [StaFact]
    public void TimecodeUpdated_AllCuesPassed_NoNextCue()
    {
        var cueManager = new StubCueManager();
        cueManager.AddCue(CreateCue("c1", triggerSeconds: 5));
        cueManager.AddCue(CreateCue("c2", triggerSeconds: 10));
        var engine = new StubTimecodeEngine();
        var vm = CreateVm(cueManager, engine);

        engine.SimulateTimecodeUpdate(TC(0, 0, 15, 0), TC(0, 0, 15, 0));

        Assert.False(vm.CueItems[0].IsNextCue);
        Assert.False(vm.CueItems[1].IsNextCue);
    }

    [StaFact]
    public void TimecodeUpdated_DisabledCueSkipped_NextEnabledCueIsNext()
    {
        var cueManager = new StubCueManager();
        cueManager.AddCue(CreateCue("c1", triggerSeconds: 5, enabled: true));
        cueManager.AddCue(CreateCue("c2", triggerSeconds: 10, enabled: false));
        cueManager.AddCue(CreateCue("c3", triggerSeconds: 15, enabled: true));
        var engine = new StubTimecodeEngine();
        var vm = CreateVm(cueManager, engine);

        // Current timecode at 7s -> c2 disabled, so next is c3
        engine.SimulateTimecodeUpdate(TC(0, 0, 7, 0), TC(0, 0, 7, 0));

        Assert.False(vm.CueItems[0].IsNextCue);
        Assert.False(vm.CueItems[1].IsNextCue); // disabled
        Assert.True(vm.CueItems[2].IsNextCue);
    }

    // --- CueItemViewModel properties ---

    [Fact]
    public void CueItemViewModel_ReflectsCueProperties()
    {
        var cueManager = new StubCueManager();
        var cue = CreateCue("c1", "My Cue", "/osc/test", 10, true);
        cue.Memo = "Some memo";
        cueManager.AddCue(cue);
        var engine = new StubTimecodeEngine();
        var vm = CreateVm(cueManager, engine);

        var item = vm.CueItems[0];
        Assert.Equal("c1", item.Id);
        Assert.Equal("My Cue", item.Name);
        Assert.Equal("Some memo", item.Memo);
        Assert.Equal(TC(0, 0, 10, 0), item.TriggerTime);
        Assert.Equal("/osc/test", item.OscAddress);
        Assert.True(item.IsEnabled);
        Assert.False(item.IsTriggered);
        Assert.False(item.IsNextCue);
    }

    // --- EditCueCommand via ICueDialogService ---

    [Fact]
    public void EditCueCommand_CallsDialogServiceWithCorrectTemplate()
    {
        var cueManager = new StubCueManager();
        var cue = CreateCue("c1", "Original", "/original", 10);
        cueManager.AddCue(cue);
        var engine = new StubTimecodeEngine();

        var dialogService = new RecordingCueDialogService();
        var vm = new CueListViewModel(cueManager, engine, new StubHostRegistry(), dialogService, new StubProjectService());

        vm.EditCueCommand.Execute("c1");

        Assert.NotNull(dialogService.LastTemplate);
        Assert.Equal("c1", dialogService.LastTemplate!.Id);
        Assert.Equal("Original", dialogService.LastTemplate.Name);
        Assert.Equal("キュー編集", dialogService.LastTitle);
    }

    [Fact]
    public void EditCueCommand_DialogConfirmed_UpdatesCueManagerAndCueItems()
    {
        var cueManager = new StubCueManager();
        cueManager.AddCue(CreateCue("c1", "Original", "/original", 10));
        var engine = new StubTimecodeEngine();

        var editedCue = CreateCue("c1", "Edited", "/edited", 20);
        var dialogService = new FixedResultCueDialogService(editedCue);
        var vm = new CueListViewModel(cueManager, engine, new StubHostRegistry(), dialogService, new StubProjectService());

        vm.EditCueCommand.Execute("c1");

        Assert.Equal("Edited", cueManager.Cues[0].Name);
        Assert.Equal("Edited", vm.CueItems[0].Name);
        Assert.Equal("/edited", vm.CueItems[0].OscAddress);
    }

    [Fact]
    public void EditCueCommand_DialogCancelled_DoesNotModifyCue()
    {
        var cueManager = new StubCueManager();
        cueManager.AddCue(CreateCue("c1", "Original", "/original", 10));
        var engine = new StubTimecodeEngine();

        var dialogService = new FixedResultCueDialogService(null); // cancel
        var vm = new CueListViewModel(cueManager, engine, new StubHostRegistry(), dialogService, new StubProjectService());

        vm.EditCueCommand.Execute("c1");

        Assert.Equal("Original", cueManager.Cues[0].Name);
        Assert.Equal("Original", vm.CueItems[0].Name);
    }

    [Fact]
    public void AddCueCommand_DialogCancelled_DoesNotAddCue()
    {
        var cueManager = new StubCueManager();
        var engine = new StubTimecodeEngine();

        var dialogService = new FixedResultCueDialogService(null); // cancel
        var vm = new CueListViewModel(cueManager, engine, new StubHostRegistry(), dialogService, new StubProjectService());

        vm.AddCueCommand.Execute(null);

        Assert.Empty(cueManager.Cues);
        Assert.Empty(vm.CueItems);
    }

    // --- DuplicateCueCommand ---

    [Fact]
    public void DuplicateCueCommand_CreatesNewCueWithCopyName()
    {
        var cueManager = new StubCueManager();
        cueManager.AddCue(CreateCue("c1", "Scene1", "/scene/1", 10));
        var engine = new StubTimecodeEngine();
        var vm = CreateVm(cueManager, engine);

        vm.DuplicateCueCommand.Execute("c1");

        Assert.Equal(2, cueManager.Cues.Count);
        Assert.Equal("Scene1 (コピー)", cueManager.Cues[1].Name);
        Assert.NotEqual("c1", cueManager.Cues[1].Id);
        Assert.Equal("/scene/1", cueManager.Cues[1].OscAddress);
    }

    // --- Dispose (event unsubscription) ---

    [StaFact]
    public void Dispose_UnsubscribesFromTimecodeUpdated()
    {
        var cueManager = new StubCueManager();
        cueManager.AddCue(CreateCue("c1", triggerSeconds: 10));
        var engine = new StubTimecodeEngine();
        var vm = CreateVm(cueManager, engine);

        vm.Dispose();

        // After dispose, timecode update should NOT update IsNextCue
        engine.SimulateTimecodeUpdate(TC(0, 0, 5, 0), TC(0, 0, 5, 0));

        // If event was properly unsubscribed, IsNextCue should remain false (initial state)
        Assert.False(vm.CueItems[0].IsNextCue);
    }

    [StaFact]
    public void Dispose_UnsubscribesFromCueTriggered()
    {
        var cueManager = new StubCueManager();
        var cue = CreateCue("c1");
        cueManager.AddCue(cue);
        var engine = new StubTimecodeEngine();
        var vm = CreateVm(cueManager, engine);

        vm.Dispose();

        // After dispose, cue triggered should NOT update IsTriggered
        cueManager.SimulateCueTriggered(cue, TC(0, 0, 10, 0), false);

        Assert.False(vm.CueItems[0].IsTriggered);
    }

    // --- Test Doubles ---

    private class StubTimecodeEngine : ITimecodeEngine
    {
        public TimecodeValue CurrentRawTimecode => default;
        public TimecodeValue CurrentOffsetTimecode => default;
        public TimecodeOffset Offset { get; set; } = TimecodeOffset.Zero(FrameRate.Fps30);
        public FrameRate FrameRate { get; set; } = FrameRate.Fps30;
        public TimecodeSourceType ActiveSource => TimecodeSourceType.Ltc;
        public bool IsReceiving => false;
        public double FreerunDurationSeconds { get; set; }
        public bool IsFreerunning => false;

        public void StartLtc(string audioDeviceId, bool isLoopback = false) { }
        public void Stop() { }
        public void StartGenerator(GeneratorSettings settings) { }
        public void ResetGenerator() { }
        public void ResetGenerator(TimecodeValue startTime) { }
        public void ResumeGenerator() { }
        public void StopGenerator() { }

        public event EventHandler<TimecodeUpdatedEventArgs>? TimecodeUpdated;
        public event EventHandler<TimecodeStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<AudioSamplesEventArgs>? AudioSamplesAvailable;

        internal void SimulateTimecodeUpdate(TimecodeValue raw, TimecodeValue offset) =>
            TimecodeUpdated?.Invoke(this, new TimecodeUpdatedEventArgs(raw, offset));

        internal void SimulateStatusChanged(bool isReceiving) =>
            StatusChanged?.Invoke(this, new TimecodeStatusChangedEventArgs(isReceiving));
    }

    private class StubHostRegistry : IHostRegistry
    {
        private readonly List<OscHost> _hosts = [];
        public IReadOnlyList<OscHost> Hosts => _hosts.AsReadOnly();
        public void AddHost(OscHost host) => _hosts.Add(host);
        public void UpdateHost(string hostId, OscHost updatedHost) { }
        public void RemoveHost(string hostId) { }
        public void SetHostEnabled(string hostId, bool enabled) { }
        public IReadOnlyList<OscHost> GetEnabledHosts(IReadOnlyList<string> hostIds) => [];
        public event EventHandler<HostChangedEventArgs>? HostChanged;
    }

    private class StubCueDialogService : ICueDialogService
    {
        public Cue? ShowEditDialog(Cue template, IReadOnlyList<OscHost> hosts, FrameRate frameRate, string title)
        {
            // Auto-confirm: return the template as-is
            return template;
        }

        public CueBatchEditResult? ShowBatchEditDialog(int cueCount, IReadOnlyList<OscHost> hosts, FrameRate frameRate) => null;

        public (int Count, TimeSpan Interval)? ShowBatchDuplicateDialog()
        {
            return null;
        }
    }

    private class RecordingCueDialogService : ICueDialogService
    {
        public Cue? LastTemplate { get; private set; }
        public string? LastTitle { get; private set; }

        public Cue? ShowEditDialog(Cue template, IReadOnlyList<OscHost> hosts, FrameRate frameRate, string title)
        {
            LastTemplate = template;
            LastTitle = title;
            return template; // auto-confirm
        }

        public CueBatchEditResult? ShowBatchEditDialog(int cueCount, IReadOnlyList<OscHost> hosts, FrameRate frameRate) => null;
        public (int Count, TimeSpan Interval)? ShowBatchDuplicateDialog() => null;
    }

    private class BatchDuplicateStubDialogService(int count, TimeSpan interval) : ICueDialogService
    {
        public Cue? ShowEditDialog(Cue template, IReadOnlyList<OscHost> hosts, FrameRate frameRate, string title) => null;
        public CueBatchEditResult? ShowBatchEditDialog(int cueCount, IReadOnlyList<OscHost> hosts, FrameRate frameRate) => null;
        public (int Count, TimeSpan Interval)? ShowBatchDuplicateDialog() => (count, interval);
    }

    private class FixedResultCueDialogService : ICueDialogService
    {
        private readonly Cue? _result;

        public FixedResultCueDialogService(Cue? result) => _result = result;

        public Cue? ShowEditDialog(Cue template, IReadOnlyList<OscHost> hosts, FrameRate frameRate, string title) => _result;
        public CueBatchEditResult? ShowBatchEditDialog(int cueCount, IReadOnlyList<OscHost> hosts, FrameRate frameRate) => null;
        public (int Count, TimeSpan Interval)? ShowBatchDuplicateDialog() => null;
    }

    private class StubCueManager : ICueManager
    {
        private readonly List<Cue> _cues = [];
        public IReadOnlyList<Cue> Cues => _cues.AsReadOnly();
    public int TriggerWindowFrames { get; set; } = 3;
    public bool IsMuted { get; set; }

        public List<string> ManualTriggerCalls { get; } = [];
        public List<(string CueId, bool Enabled)> SetCueEnabledCalls { get; } = [];

        public void AddCue(Cue cue) => _cues.Add(cue);

        public void UpdateCue(string cueId, Cue updatedCue)
        {
            var index = _cues.FindIndex(c => c.Id == cueId);
            if (index >= 0) _cues[index] = updatedCue;
        }

        public void RemoveCue(string cueId) =>
            _cues.RemoveAll(c => c.Id == cueId);

        public void ReorderCues(IReadOnlyList<string> orderedCueIds) { }

        public void SetCueEnabled(string cueId, bool enabled)
        {
            SetCueEnabledCalls.Add((cueId, enabled));
            var cue = _cues.FirstOrDefault(c => c.Id == cueId);
            if (cue != null) cue.IsEnabled = enabled;
        }

        public void ManualTrigger(string cueId) => ManualTriggerCalls.Add(cueId);

        public void ResetTracking() { }
        public void SendCueSync(string oscAddress, IReadOnlyList<string> targetHostIds) { }

        public event EventHandler<CueTriggeredEventArgs>? CueTriggered;

        internal void SimulateCueTriggered(Cue cue, TimecodeValue triggerTimecode, bool isManual) =>
            CueTriggered?.Invoke(this, new CueTriggeredEventArgs
            {
                Cue = cue,
                TriggerTimecode = triggerTimecode,
                IsManual = isManual,
            });
    }
}
