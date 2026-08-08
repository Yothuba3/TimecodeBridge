namespace TimecodeBridge.Tests.ViewModels;

using TimecodeBridge.Models;
using TimecodeBridge.Services;
using TimecodeBridge.Services.Interfaces;
using TimecodeBridge.ViewModels;

public class TimecodeViewModelTests
{
    private static TimecodeValue TC(int h, int m, int s, int f) =>
        new(h, m, s, f, FrameRate.Fps30);

    private static ICueManager StubCue() => new StubCueManagerForTc();

    private class StubCueManagerForTc : ICueManager
    {
        public IReadOnlyList<Cue> Cues => [];
        public int TriggerWindowFrames { get; set; } = 3;
        public bool IsMuted { get; set; }
        public void AddCue(Cue cue) { }
        public void UpdateCue(string cueId, Cue updatedCue) { }
        public void RemoveCue(string cueId) { }
        public void ReorderCues(IReadOnlyList<string> orderedCueIds) { }
        public void SetCueEnabled(string cueId, bool enabled) { }
        public void ManualTrigger(string cueId) { }
        public event EventHandler<CueTriggeredEventArgs>? CueTriggered;
    }

    // --- Construction ---

    [Fact]
    public void Constructor_InitializesDefaultValues()
    {
        var engine = new StubTimecodeEngine();
        var vm = new TimecodeViewModel(engine, StubCue(), new StubProjectService());

        Assert.Equal("", vm.RawTimecodeDisplay);
        Assert.Equal("", vm.OffsetTimecodeDisplay);
        Assert.False(vm.IsReceiving);
        Assert.Equal("停止", vm.StatusText);
    }

    // --- TimecodeUpdated event ---

    [StaFact]
    public void TimecodeUpdated_UpdatesRawTimecodeDisplay()
    {
        var engine = new StubTimecodeEngine();
        var vm = new TimecodeViewModel(engine, StubCue(), new StubProjectService());

        engine.SimulateTimecodeUpdate(TC(1, 2, 3, 4), TC(1, 2, 3, 4));

        Assert.Equal("01:02:03:04", vm.RawTimecodeDisplay);
    }

    [StaFact]
    public void TimecodeUpdated_UpdatesOffsetTimecodeDisplay()
    {
        var engine = new StubTimecodeEngine();
        var vm = new TimecodeViewModel(engine, StubCue(), new StubProjectService());

        engine.SimulateTimecodeUpdate(TC(1, 0, 0, 0), TC(2, 0, 0, 0));

        Assert.Equal("02:00:00:00", vm.OffsetTimecodeDisplay);
    }

    [StaFact]
    public void TimecodeUpdated_RaisesPropertyChanged()
    {
        var engine = new StubTimecodeEngine();
        var vm = new TimecodeViewModel(engine, StubCue(), new StubProjectService());

        var changedProperties = new List<string>();
        vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName!);

        engine.SimulateTimecodeUpdate(TC(0, 0, 1, 0), TC(0, 0, 1, 0));

        Assert.Contains("RawTimecodeDisplay", changedProperties);
        Assert.Contains("OffsetTimecodeDisplay", changedProperties);
    }

    // --- StatusChanged event ---

    [StaFact]
    public void StatusChanged_Receiving_UpdatesIsReceivingAndStatusText()
    {
        var engine = new StubTimecodeEngine();
        var vm = new TimecodeViewModel(engine, StubCue(), new StubProjectService());

        engine.SimulateStatusChanged(true);

        Assert.True(vm.IsReceiving);
        Assert.Equal("受信中", vm.StatusText);
    }

    [StaFact]
    public void StatusChanged_NotReceiving_UpdatesIsReceivingAndStatusText()
    {
        var engine = new StubTimecodeEngine();
        var vm = new TimecodeViewModel(engine, StubCue(), new StubProjectService());

        // First set to receiving
        engine.SimulateStatusChanged(true);
        Assert.True(vm.IsReceiving);

        // Then stop
        engine.SimulateStatusChanged(false);
        Assert.False(vm.IsReceiving);
        Assert.Equal("信号喪失", vm.StatusText);
    }

    [StaFact]
    public void StatusChanged_RaisesPropertyChanged()
    {
        var engine = new StubTimecodeEngine();
        var vm = new TimecodeViewModel(engine, StubCue(), new StubProjectService());

        var changedProperties = new List<string>();
        vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName!);

        engine.SimulateStatusChanged(true);

        Assert.Contains("IsReceiving", changedProperties);
        Assert.Contains("StatusText", changedProperties);
    }

    // --- Offset property ---

    [Fact]
    public void Offset_SetValue_SyncsToEngine()
    {
        var engine = new StubTimecodeEngine();
        var vm = new TimecodeViewModel(engine, StubCue(), new StubProjectService());

        var offset = new TimecodeOffset(false, 0, 0, 5, 0, FrameRate.Fps30);
        vm.Offset = offset;

        Assert.Equal(offset, engine.Offset);
    }

    [Fact]
    public void Offset_DefaultValue_MatchesEngineDefault()
    {
        var engine = new StubTimecodeEngine();
        var vm = new TimecodeViewModel(engine, StubCue(), new StubProjectService());

        Assert.Equal(engine.Offset, vm.Offset);
    }

    // --- DropFrame display ---

    [StaFact]
    public void TimecodeUpdated_DropFrame_DisplaysWithSemicolon()
    {
        var engine = new StubTimecodeEngine();
        var vm = new TimecodeViewModel(engine, StubCue(), new StubProjectService());

        var raw = new TimecodeValue(1, 0, 0, 2, FrameRate.Fps2997Drop);
        var offset = new TimecodeValue(1, 0, 0, 2, FrameRate.Fps2997Drop);
        engine.SimulateTimecodeUpdate(raw, offset);

        Assert.Equal("01:00:00;02", vm.RawTimecodeDisplay);
        Assert.Equal("01:00:00;02", vm.OffsetTimecodeDisplay);
    }

    // --- StatusText initial state ---

    [StaFact]
    public void StatusChanged_NeverReceivedThenLostSignal_ShowsSignalLost()
    {
        var engine = new StubTimecodeEngine();
        var vm = new TimecodeViewModel(engine, StubCue(), new StubProjectService());

        // Simulate receiving first, then losing signal
        engine.SimulateStatusChanged(true);
        engine.SimulateStatusChanged(false);

        Assert.Equal("信号喪失", vm.StatusText);
    }

    // --- Dispose (event unsubscription) ---

    [StaFact]
    public void Dispose_UnsubscribesFromTimecodeUpdated()
    {
        var engine = new StubTimecodeEngine();
        var vm = new TimecodeViewModel(engine, StubCue(), new StubProjectService());

        // Verify update works before dispose
        engine.SimulateTimecodeUpdate(TC(1, 0, 0, 0), TC(1, 0, 0, 0));
        Assert.Equal("01:00:00:00", vm.RawTimecodeDisplay);

        vm.Dispose();

        // After dispose, timecode update should NOT update the display
        engine.SimulateTimecodeUpdate(TC(2, 0, 0, 0), TC(2, 0, 0, 0));
        Assert.Equal("01:00:00:00", vm.RawTimecodeDisplay);
    }

    [StaFact]
    public void Dispose_UnsubscribesFromStatusChanged()
    {
        var engine = new StubTimecodeEngine();
        var vm = new TimecodeViewModel(engine, StubCue(), new StubProjectService());

        engine.SimulateStatusChanged(true);
        Assert.True(vm.IsReceiving);

        vm.Dispose();

        // After dispose, status change should NOT update the VM
        engine.SimulateStatusChanged(false);
        Assert.True(vm.IsReceiving); // should remain true
    }

    // --- IsTriggerMuted syncs to CueManager ---

    [Fact]
    public void IsTriggerMuted_SyncsToCueManager()
    {
        var engine = new StubTimecodeEngine();
        var cueManager = new MutableStubCueManager();
        var vm = new TimecodeViewModel(engine, cueManager, new StubProjectService());

        vm.IsTriggerMuted = true;
        Assert.True(cueManager.IsMuted);

        vm.IsTriggerMuted = false;
        Assert.False(cueManager.IsMuted);
    }

    private class MutableStubCueManager : ICueManager
    {
        public IReadOnlyList<Cue> Cues => [];
        public int TriggerWindowFrames { get; set; } = 3;
        public bool IsMuted { get; set; }
        public void AddCue(Cue cue) { }
        public void UpdateCue(string cueId, Cue updatedCue) { }
        public void RemoveCue(string cueId) { }
        public void ReorderCues(IReadOnlyList<string> orderedCueIds) { }
        public void SetCueEnabled(string cueId, bool enabled) { }
        public void ManualTrigger(string cueId) { }
        public event EventHandler<CueTriggeredEventArgs>? CueTriggered;
    }

    // --- Test Double ---

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
}
