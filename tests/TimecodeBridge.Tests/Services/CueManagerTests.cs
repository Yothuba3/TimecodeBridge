namespace TimecodeBridge.Tests.Services;

using TimecodeBridge.Models;
using TimecodeBridge.Services;
using TimecodeBridge.Services.Interfaces;

public class CueManagerTests
{
    private readonly CueManager _manager;

    public CueManagerTests()
    {
        _manager = new CueManager(new StubTimecodeEngine(), new StubOscSender());
    }

    private static Cue CreateCue(string id = "cue-1", string name = "Test Cue",
        string oscAddress = "/test", bool enabled = true)
    {
        return new Cue
        {
            Id = id,
            Name = name,
            TriggerTime = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30),
            OscAddress = oscAddress,
            IsEnabled = enabled,
        };
    }

    // --- AddCue ---

    [Fact]
    public void AddCue_AddsToList()
    {
        var cue = CreateCue();
        _manager.AddCue(cue);

        Assert.Single(_manager.Cues);
        Assert.Equal("cue-1", _manager.Cues[0].Id);
    }

    [Fact]
    public void AddCue_MultipleCues_AllAppearInList()
    {
        _manager.AddCue(CreateCue("c1"));
        _manager.AddCue(CreateCue("c2"));
        _manager.AddCue(CreateCue("c3"));

        Assert.Equal(3, _manager.Cues.Count);
    }

    [Fact]
    public void AddCue_DuplicateId_ThrowsArgumentException()
    {
        _manager.AddCue(CreateCue("c1"));

        Assert.Throws<ArgumentException>(() => _manager.AddCue(CreateCue("c1")));
    }

    // --- UpdateCue ---

    [Fact]
    public void UpdateCue_UpdatesExistingCue()
    {
        _manager.AddCue(CreateCue("c1", "Old Name"));
        var updated = CreateCue("c1", "New Name", "/new-address");

        _manager.UpdateCue("c1", updated);

        Assert.Equal("New Name", _manager.Cues[0].Name);
        Assert.Equal("/new-address", _manager.Cues[0].OscAddress);
    }

    [Fact]
    public void UpdateCue_NonExistentId_ThrowsKeyNotFoundException()
    {
        Assert.Throws<KeyNotFoundException>(() =>
            _manager.UpdateCue("nonexistent", CreateCue()));
    }

    // --- RemoveCue ---

    [Fact]
    public void RemoveCue_RemovesFromList()
    {
        _manager.AddCue(CreateCue("c1"));
        _manager.AddCue(CreateCue("c2"));

        _manager.RemoveCue("c1");

        Assert.Single(_manager.Cues);
        Assert.Equal("c2", _manager.Cues[0].Id);
    }

    [Fact]
    public void RemoveCue_NonExistentId_ThrowsKeyNotFoundException()
    {
        Assert.Throws<KeyNotFoundException>(() => _manager.RemoveCue("nonexistent"));
    }

    // --- ReorderCues ---

    [Fact]
    public void ReorderCues_ReordersToSpecifiedOrder()
    {
        _manager.AddCue(CreateCue("c1"));
        _manager.AddCue(CreateCue("c2"));
        _manager.AddCue(CreateCue("c3"));

        _manager.ReorderCues(["c3", "c1", "c2"]);

        Assert.Equal("c3", _manager.Cues[0].Id);
        Assert.Equal("c1", _manager.Cues[1].Id);
        Assert.Equal("c2", _manager.Cues[2].Id);
    }

    // --- SetCueEnabled ---

    [Fact]
    public void SetCueEnabled_TogglesEnabledState()
    {
        _manager.AddCue(CreateCue("c1", enabled: true));

        _manager.SetCueEnabled("c1", false);
        Assert.False(_manager.Cues[0].IsEnabled);

        _manager.SetCueEnabled("c1", true);
        Assert.True(_manager.Cues[0].IsEnabled);
    }

    // --- Cues ---

    [Fact]
    public void Cues_ReturnsReadOnlyList()
    {
        _manager.AddCue(CreateCue("c1"));

        var cues = _manager.Cues;

        Assert.IsAssignableFrom<IReadOnlyList<Cue>>(cues);
    }

    // --- Task 6.2: Timecode Range Trigger ---

    [Fact]
    public void TimecodeUpdated_TriggerTimeMatches_CueIsTriggered()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        var cue = CreateCue("c1");
        cue.TriggerTime = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30);
        cue.OscAddress = "/trigger";
        cue.Arguments = [new OscInt32Argument(1)];
        cue.TargetHostIds = ["host1"];
        manager.AddCue(cue);

        // First update: sets _lastTimecode to frame 0:0:5:0
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30));

        // Second update: advances to 0:0:10:0, cue.TriggerTime is in range (5:0, 10:0]
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30));

        Assert.Single(spySender.SendCalls);
        Assert.Equal("/trigger", spySender.SendCalls[0].OscAddress);
        Assert.Single(spySender.SendCalls[0].Arguments);
        Assert.Equal("host1", spySender.SendCalls[0].TargetHostIds[0]);
    }

    [Fact]
    public void TimecodeUpdated_FrameSkip_MultipleCuesInRangeTriggered()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        var cue1 = CreateCue("c1");
        cue1.TriggerTime = new TimecodeValue(0, 0, 3, 0, FrameRate.Fps30);
        manager.AddCue(cue1);

        var cue2 = CreateCue("c2");
        cue2.TriggerTime = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30);
        manager.AddCue(cue2);

        var cue3 = CreateCue("c3");
        cue3.TriggerTime = new TimecodeValue(0, 0, 8, 0, FrameRate.Fps30);
        manager.AddCue(cue3);

        // First update: set _lastTimecode to 0:0:1:0
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 1, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 1, 0, FrameRate.Fps30));

        // Skip frames: jump from 0:0:1:0 to 0:0:9:0 -> cue1(3:0) and cue2(5:0) and cue3(8:0) all in range
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 9, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 9, 0, FrameRate.Fps30));

        Assert.Equal(3, spySender.SendCalls.Count);
    }

    [Fact]
    public void TimecodeUpdated_Reverse_NoCueTrigger()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        var cue = CreateCue("c1");
        cue.TriggerTime = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30);
        manager.AddCue(cue);

        // Set _lastTimecode to 0:0:10:0
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30));

        // Reverse to 0:0:3:0 — should NOT trigger any cue
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 3, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 3, 0, FrameRate.Fps30));

        Assert.Empty(spySender.SendCalls);
    }

    [Fact]
    public void TimecodeUpdated_ReverseAndThenForward_CueTriggersCorrectly()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        var cue = CreateCue("c1");
        cue.TriggerTime = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30);
        manager.AddCue(cue);

        // Set _lastTimecode to 0:0:10:0
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30));

        // Reverse to 0:0:3:0 — _lastTimecode resets to 0:0:3:0
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 3, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 3, 0, FrameRate.Fps30));

        // Forward to 0:0:6:0 — cue at 5:0 is in range (3:0, 6:0]
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 6, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 6, 0, FrameRate.Fps30));

        Assert.Single(spySender.SendCalls);
    }

    [Fact]
    public void TimecodeUpdated_DisabledCue_IsSkipped()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        var cue = CreateCue("c1", enabled: false);
        cue.TriggerTime = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30);
        manager.AddCue(cue);

        // Set _lastTimecode
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 1, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 1, 0, FrameRate.Fps30));

        // Advance past cue trigger time
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30));

        Assert.Empty(spySender.SendCalls);
    }

    [Fact]
    public void TimecodeUpdated_OscSendCalledWithCorrectArguments()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        var cue = CreateCue("c1");
        cue.TriggerTime = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30);
        cue.OscAddress = "/cue/fire";
        cue.Arguments = [new OscFloat32Argument(0.5f), new OscStringArgument("go")];
        cue.TargetHostIds = ["h1", "h2"];
        manager.AddCue(cue);

        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 1, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 1, 0, FrameRate.Fps30));

        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30));

        var call = Assert.Single(spySender.SendCalls);
        Assert.Equal("/cue/fire", call.OscAddress);
        Assert.Equal(2, call.Arguments.Count);
        Assert.Equal(2, call.TargetHostIds.Count);
        Assert.Equal("h1", call.TargetHostIds[0]);
        Assert.Equal("h2", call.TargetHostIds[1]);
    }

    [Fact]
    public void TimecodeUpdated_CueTriggeredEventFired_WithIsManualFalse()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        var cue = CreateCue("c1");
        cue.TriggerTime = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30);
        manager.AddCue(cue);

        CueTriggeredEventArgs? firedArgs = null;
        manager.CueTriggered += (_, args) => firedArgs = args;

        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 1, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 1, 0, FrameRate.Fps30));

        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30));

        Assert.NotNull(firedArgs);
        Assert.Equal("c1", firedArgs.Cue.Id);
        Assert.False(firedArgs.IsManual);
        Assert.Equal(new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30), firedArgs.TriggerTimecode);
    }

    [Fact]
    public void TimecodeUpdated_FirstUpdate_OnlyExactMatchTriggers()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        var cueExact = CreateCue("c1");
        cueExact.TriggerTime = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30);
        manager.AddCue(cueExact);

        var cueBefore = CreateCue("c2");
        cueBefore.TriggerTime = new TimecodeValue(0, 0, 3, 0, FrameRate.Fps30);
        manager.AddCue(cueBefore);

        // First update ever — only exact match (0:0:5:0) should trigger
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30));

        Assert.Single(spySender.SendCalls);
        Assert.Equal(cueExact.OscAddress, spySender.SendCalls[0].OscAddress);
    }

    // --- Task 6.3: Manual Trigger ---

    [Fact]
    public void ManualTrigger_SendsOscMessage()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        var cue = CreateCue("c1");
        cue.OscAddress = "/manual";
        cue.Arguments = [new OscInt32Argument(42)];
        cue.TargetHostIds = ["host1"];
        manager.AddCue(cue);

        manager.ManualTrigger("c1");

        var call = Assert.Single(spySender.SendCalls);
        Assert.Equal("/manual", call.OscAddress);
        Assert.Single(call.Arguments);
        Assert.Equal("host1", call.TargetHostIds[0]);
    }

    [Fact]
    public void ManualTrigger_CueTriggeredEventFired_WithIsManualTrue()
    {
        var stubEngine = new StubTimecodeEngine();
        stubEngine.SetCurrentOffsetTimecode(new TimecodeValue(0, 1, 0, 0, FrameRate.Fps30));
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        var cue = CreateCue("c1");
        manager.AddCue(cue);

        CueTriggeredEventArgs? firedArgs = null;
        manager.CueTriggered += (_, args) => firedArgs = args;

        manager.ManualTrigger("c1");

        Assert.NotNull(firedArgs);
        Assert.Equal("c1", firedArgs.Cue.Id);
        Assert.True(firedArgs.IsManual);
        Assert.Equal(new TimecodeValue(0, 1, 0, 0, FrameRate.Fps30), firedArgs.TriggerTimecode);
    }

    [Fact]
    public void ManualTrigger_NonExistentCueId_ThrowsKeyNotFoundException()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        Assert.Throws<KeyNotFoundException>(() => manager.ManualTrigger("nonexistent"));
    }

    [Fact]
    public void ManualTrigger_DisabledCue_StillTriggered()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        var cue = CreateCue("c1", enabled: false);
        cue.OscAddress = "/disabled-manual";
        manager.AddCue(cue);

        manager.ManualTrigger("c1");

        Assert.Single(spySender.SendCalls);
        Assert.Equal("/disabled-manual", spySender.SendCalls[0].OscAddress);
    }

    // --- TriggerOffset（発火タイミングのオフセット） ---

    [Fact]
    public void TriggerOffset_Positive_ShiftsFiringLater()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        var cue = CreateCue("c1");
        cue.TriggerTime = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30);
        cue.TriggerOffset = new TimecodeOffset(false, 0, 0, 2, 0, FrameRate.Fps30); // +2秒
        manager.AddCue(cue);

        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 1, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 1, 0, FrameRate.Fps30));

        // トリガー時間(5秒)を過ぎても実効発火時刻(7秒)前なので発火しない
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 6, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 6, 0, FrameRate.Fps30));
        Assert.Empty(spySender.SendCalls);

        // 実効発火時刻(7秒)を跨いだら発火する
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 8, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 8, 0, FrameRate.Fps30));
        Assert.Single(spySender.SendCalls);
    }

    [Fact]
    public void TriggerOffset_Negative_ShiftsFiringEarlier()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        var cue = CreateCue("c1");
        cue.TriggerTime = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30);
        cue.TriggerOffset = new TimecodeOffset(true, 0, 0, 2, 0, FrameRate.Fps30); // -2秒
        manager.AddCue(cue);

        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 1, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 1, 0, FrameRate.Fps30));

        // 実効発火時刻(3秒)を跨いだら、トリガー時間(5秒)前でも発火する
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 4, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 4, 0, FrameRate.Fps30));
        Assert.Single(spySender.SendCalls);
    }

    [Fact]
    public void UpdateCue_FiredCueMovedToFuture_FiresAgain()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        var cue = CreateCue("c1");
        cue.TriggerTime = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30);
        manager.AddCue(cue);

        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30));
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 12, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 12, 0, FrameRate.Fps30));
        Assert.Single(spySender.SendCalls); // 1回目の発火

        // 発火済みキューをオフセット編集で未来(0:0:20)へ移す
        var updated = CreateCue("c1");
        updated.TriggerTime = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30);
        updated.TriggerOffset = new TimecodeOffset(false, 0, 0, 10, 0, FrameRate.Fps30);
        manager.UpdateCue("c1", updated);

        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 25, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 25, 0, FrameRate.Fps30));

        Assert.Equal(2, spySender.SendCalls.Count); // 新しい実効時刻で再発火
    }

    // --- SendTimecode（秒数送信で送るタイムコード） ---

    [Fact]
    public void SendTimecode_Specified_SentAsSecondsInsteadOfTriggerTime()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        var cue = CreateCue("c1");
        cue.TriggerTime = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30);
        cue.SendTriggerTimeAsSeconds = true;
        cue.SendTimecode = new TimecodeValue(0, 10, 0, 0, FrameRate.Fps30); // 600秒
        manager.AddCue(cue);

        manager.ManualTrigger("c1");

        var call = Assert.Single(spySender.SendCalls);
        var seconds = Assert.IsType<OscFloat32Argument>(call.Arguments[0]).Value;
        Assert.Equal(600f, seconds);
    }

    [Fact]
    public void SendTimecode_NotSpecified_TriggerTimeSentAsSeconds()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        var cue = CreateCue("c1");
        cue.TriggerTime = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30);
        cue.SendTriggerTimeAsSeconds = true;
        manager.AddCue(cue);

        manager.ManualTrigger("c1");

        var call = Assert.Single(spySender.SendCalls);
        var seconds = Assert.IsType<OscFloat32Argument>(call.Arguments[0]).Value;
        Assert.Equal(5f, seconds);
    }

    // --- Test Doubles ---

    private class StubTimecodeEngine : ITimecodeEngine
    {
        private TimecodeValue _currentOffsetTimecode;

        public TimecodeValue CurrentRawTimecode => default;
        public TimecodeValue CurrentOffsetTimecode => _currentOffsetTimecode;
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

        internal void SetCurrentOffsetTimecode(TimecodeValue value) => _currentOffsetTimecode = value;

        internal void SimulateTimecodeUpdate(TimecodeValue raw, TimecodeValue offset) =>
            TimecodeUpdated?.Invoke(this, new TimecodeUpdatedEventArgs(raw, offset));

        // Suppress unused event warnings
        internal void RaiseTimecodeUpdated(TimecodeUpdatedEventArgs args) =>
            TimecodeUpdated?.Invoke(this, args);
        internal void RaiseStatusChanged(TimecodeStatusChangedEventArgs args) =>
            StatusChanged?.Invoke(this, args);
    }

    private class SpyOscSender : IOscSender
    {
        public List<OscSendCall> SendCalls { get; } = [];

        public void Send(string oscAddress, IReadOnlyList<OscArgument> arguments, IReadOnlyList<string> targetHostIds)
        {
            SendCalls.Add(new OscSendCall(oscAddress, arguments, targetHostIds));
        }

        public void SendPing(string hostId) { }
        public Task SendIcmpPingAsync(string hostId, int framesPerSecond) => Task.CompletedTask;

        public event EventHandler<OscSendResultEventArgs>? SendCompleted;

        // Suppress unused event warning
        internal void RaiseSendCompleted(OscSendResultEventArgs args) =>
            SendCompleted?.Invoke(this, args);

        public record OscSendCall(string OscAddress, IReadOnlyList<OscArgument> Arguments, IReadOnlyList<string> TargetHostIds);
    }

    private class StubOscSender : IOscSender
    {
        public void Send(string oscAddress, IReadOnlyList<OscArgument> arguments, IReadOnlyList<string> targetHostIds) { }
        public void SendPing(string hostId) { }
        public Task SendIcmpPingAsync(string hostId, int framesPerSecond) => Task.CompletedTask;

        public event EventHandler<OscSendResultEventArgs>? SendCompleted;

        // Suppress unused event warning
        internal void RaiseSendCompleted(OscSendResultEventArgs args) =>
            SendCompleted?.Invoke(this, args);
    }
}
