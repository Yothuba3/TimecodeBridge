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
    public void TimecodeUpdated_FrameSkipWithinWindow_CueInRangeTriggered()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender); // 判定幅 既定3

        var cue = CreateCue("c1");
        cue.TriggerTime = new TimecodeValue(0, 0, 5, 1, FrameRate.Fps30);
        manager.AddCue(cue);

        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30),
            new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30));

        // 判定幅(3)以内の3フレームスキップ: 5:00 → 5:03。間の 5:01 のキューは発火する
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 5, 3, FrameRate.Fps30),
            new TimecodeValue(0, 0, 5, 3, FrameRate.Fps30));

        Assert.Single(spySender.SendCalls);
    }

    [Fact]
    public void TimecodeUpdated_JumpBeyondWindow_IntermediateCuesNotTriggered()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender); // 判定幅 既定3

        var cue1 = CreateCue("c1");
        cue1.TriggerTime = new TimecodeValue(4, 0, 0, 0, FrameRate.Fps30);
        manager.AddCue(cue1);

        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(3, 50, 0, 0, FrameRate.Fps30),
            new TimecodeValue(3, 50, 0, 0, FrameRate.Fps30));

        // 判定幅を超える前方ジャンプ(3:50 → 4:30)はシーク扱い: 途中の 4:00 のキューは発火しない
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(4, 30, 0, 0, FrameRate.Fps30),
            new TimecodeValue(4, 30, 0, 0, FrameRate.Fps30));

        Assert.Empty(spySender.SendCalls);

        // ジャンプ後、通常再生で前を通過すれば以後のキューは普通に発火する
        var cue2 = CreateCue("c2");
        cue2.TriggerTime = new TimecodeValue(4, 30, 0, 2, FrameRate.Fps30);
        manager.AddCue(cue2);

        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(4, 30, 0, 1, FrameRate.Fps30),
            new TimecodeValue(4, 30, 0, 1, FrameRate.Fps30));
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(4, 30, 0, 2, FrameRate.Fps30),
            new TimecodeValue(4, 30, 0, 2, FrameRate.Fps30));

        Assert.Single(spySender.SendCalls);
    }

    [Fact]
    public void TimecodeUpdated_JumpLandsExactlyOnCue_Triggered()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        var cue = CreateCue("c1");
        cue.TriggerTime = new TimecodeValue(4, 30, 0, 0, FrameRate.Fps30);
        manager.AddCue(cue);

        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(3, 50, 0, 0, FrameRate.Fps30),
            new TimecodeValue(3, 50, 0, 0, FrameRate.Fps30));

        // ジャンプの着地フレームちょうどのキューは（受信開始時の完全一致と同様に）発火する
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(4, 30, 0, 0, FrameRate.Fps30),
            new TimecodeValue(4, 30, 0, 0, FrameRate.Fps30));

        Assert.Single(spySender.SendCalls);
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

        // 4:29までシーク後、判定幅内の前進(4:29→5:01)でキュー5:0を通過発火
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 4, 29, FrameRate.Fps30),
            new TimecodeValue(0, 0, 4, 29, FrameRate.Fps30));
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 5, 1, FrameRate.Fps30),
            new TimecodeValue(0, 0, 5, 1, FrameRate.Fps30));

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

        // トリガー時間(5秒)を過ぎた6:28に位置しても、実効発火時刻(7秒)前なので発火しない
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 6, 28, FrameRate.Fps30),
            new TimecodeValue(0, 0, 6, 28, FrameRate.Fps30));
        Assert.Empty(spySender.SendCalls);

        // 実効発火時刻(7秒)を判定幅内の前進で跨いだら発火する
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 7, 1, FrameRate.Fps30),
            new TimecodeValue(0, 0, 7, 1, FrameRate.Fps30));
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
            new TimecodeValue(0, 0, 2, 28, FrameRate.Fps30),
            new TimecodeValue(0, 0, 2, 28, FrameRate.Fps30));

        // 実効発火時刻(3秒)を跨いだら、トリガー時間(5秒)前でも発火する
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 3, 1, FrameRate.Fps30),
            new TimecodeValue(0, 0, 3, 1, FrameRate.Fps30));
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
            new TimecodeValue(0, 0, 9, 28, FrameRate.Fps30),
            new TimecodeValue(0, 0, 9, 28, FrameRate.Fps30));
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 10, 1, FrameRate.Fps30),
            new TimecodeValue(0, 0, 10, 1, FrameRate.Fps30));
        Assert.Single(spySender.SendCalls); // 1回目の発火

        // 発火済みキューをオフセット編集で未来(0:0:20)へ移す
        var updated = CreateCue("c1");
        updated.TriggerTime = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30);
        updated.TriggerOffset = new TimecodeOffset(false, 0, 0, 10, 0, FrameRate.Fps30);
        manager.UpdateCue("c1", updated);

        // 19:28までシーク後、判定幅内の前進で新しい実効時刻(20:0)を跨ぐ
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 19, 28, FrameRate.Fps30),
            new TimecodeValue(0, 0, 19, 28, FrameRate.Fps30));
        stubEngine.SimulateTimecodeUpdate(
            new TimecodeValue(0, 0, 20, 1, FrameRate.Fps30),
            new TimecodeValue(0, 0, 20, 1, FrameRate.Fps30));

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

    // --- AdditionalOscAddresses（追加アドレスは引数なしで送信） ---

    [Fact]
    public void ManualTrigger_AdditionalAddresses_SentWithoutArguments()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        var cue = CreateCue("c1");
        cue.OscAddress = "/main";
        cue.Arguments = [new OscInt32Argument(1)];
        cue.AdditionalOscAddresses = ["/extra/1", "/extra/2"];
        cue.TargetHostIds = ["host1"];
        manager.AddCue(cue);

        manager.ManualTrigger("c1");

        Assert.Equal(3, spySender.SendCalls.Count);
        Assert.Equal("/main", spySender.SendCalls[0].OscAddress);
        Assert.Single(spySender.SendCalls[0].Arguments);
        Assert.Equal("/extra/1", spySender.SendCalls[1].OscAddress);
        Assert.Empty(spySender.SendCalls[1].Arguments); // 追加アドレスは引数なし
        Assert.Equal("/extra/2", spySender.SendCalls[2].OscAddress);
        Assert.Empty(spySender.SendCalls[2].Arguments);
        Assert.All(spySender.SendCalls, c => Assert.Equal("host1", c.TargetHostIds[0]));
    }

    // --- SendCueSync（Cue-Syncワンショット送信） ---

    [Fact]
    public void SendCueSync_NearestPrecedingCueUsedAsBase()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        var cueA = CreateCue("a");
        cueA.TriggerTime = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30);
        cueA.SendTimecode = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30);
        manager.AddCue(cueA);

        var cueB = CreateCue("b");
        cueB.TriggerTime = new TimecodeValue(0, 0, 20, 0, FrameRate.Fps30);
        cueB.SendTimecode = new TimecodeValue(2, 0, 0, 0, FrameRate.Fps30);
        manager.AddCue(cueB);

        // 現在 0:00:25 → 直前は cueB(20秒)。送信値 = 2:00:00:00 + 5秒 = 7205秒
        stubEngine.SetCurrentOffsetTimecode(new TimecodeValue(0, 0, 25, 0, FrameRate.Fps30));
        manager.SendCueSync("/cuesync", ["host1"]);

        var call = Assert.Single(spySender.SendCalls);
        Assert.Equal("/cuesync", call.OscAddress);
        Assert.Equal("host1", call.TargetHostIds[0]);
        Assert.Equal(7205f, Assert.IsType<OscFloat32Argument>(call.Arguments[0]).Value);
    }

    [Fact]
    public void SendCueSync_DisabledCue_Ignored()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        // 無効 → 基準候補にならない
        var disabled = CreateCue("disabled", enabled: false);
        disabled.TriggerTime = new TimecodeValue(0, 0, 23, 0, FrameRate.Fps30);
        disabled.SendTimecode = new TimecodeValue(9, 0, 0, 0, FrameRate.Fps30);
        manager.AddCue(disabled);

        var baseCue = CreateCue("base");
        baseCue.TriggerTime = new TimecodeValue(0, 0, 20, 0, FrameRate.Fps30);
        baseCue.SendTimecode = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30);
        manager.AddCue(baseCue);

        stubEngine.SetCurrentOffsetTimecode(new TimecodeValue(0, 0, 25, 0, FrameRate.Fps30));
        manager.SendCueSync("/cuesync", ["host1"]);

        var call = Assert.Single(spySender.SendCalls);
        Assert.Equal(3605f, Assert.IsType<OscFloat32Argument>(call.Arguments[0]).Value);
    }

    [Fact]
    public void SendCueSync_BaseCueWithoutSendTimecode_UsesTriggerTimeAsAxis()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        // 送信TC未指定・トリガーオフセット+2秒 → 実効22秒、送信軸はトリガー時間(20秒)
        var cue = CreateCue("c1");
        cue.TriggerTime = new TimecodeValue(0, 0, 20, 0, FrameRate.Fps30);
        cue.TriggerOffset = new TimecodeOffset(false, 0, 0, 2, 0, FrameRate.Fps30);
        manager.AddCue(cue);

        // 現在25秒 → 20 + (25 - 22) = 23秒（= 現在TCからオフセット分を差し引いた値）
        stubEngine.SetCurrentOffsetTimecode(new TimecodeValue(0, 0, 25, 0, FrameRate.Fps30));
        manager.SendCueSync("/cuesync", ["host1"]);

        var call = Assert.Single(spySender.SendCalls);
        Assert.Equal(23f, Assert.IsType<OscFloat32Argument>(call.Arguments[0]).Value);
    }

    [Fact]
    public void SendCueSync_NoSendTimecodeCueIsNearest_TakesPrecedence()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        // 送信TC指定ありだが遠いキュー
        var withSendTc = CreateCue("with");
        withSendTc.TriggerTime = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30);
        withSendTc.SendTimecode = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30);
        manager.AddCue(withSendTc);

        // 送信TC未指定だが直前のキュー → こちらが基準
        var nearest = CreateCue("nearest");
        nearest.TriggerTime = new TimecodeValue(0, 0, 24, 0, FrameRate.Fps30);
        manager.AddCue(nearest);

        // 現在25秒 → 24 + (25 - 24) = 25秒
        stubEngine.SetCurrentOffsetTimecode(new TimecodeValue(0, 0, 25, 0, FrameRate.Fps30));
        manager.SendCueSync("/cuesync", ["host1"]);

        var call = Assert.Single(spySender.SendCalls);
        Assert.Equal(25f, Assert.IsType<OscFloat32Argument>(call.Arguments[0]).Value);
    }

    [Fact]
    public void SendCueSync_NoPrecedingCue_SendsZero()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        var cue = CreateCue("future");
        cue.TriggerTime = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30);
        cue.SendTimecode = new TimecodeValue(2, 0, 0, 0, FrameRate.Fps30);
        manager.AddCue(cue);

        stubEngine.SetCurrentOffsetTimecode(new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30));
        manager.SendCueSync("/cuesync", ["host1"]);

        var call = Assert.Single(spySender.SendCalls);
        Assert.Equal(0f, Assert.IsType<OscFloat32Argument>(call.Arguments[0]).Value);
    }

    [Fact]
    public void SendCueSync_TriggerOffsetShiftsBaseTime()
    {
        var stubEngine = new StubTimecodeEngine();
        var spySender = new SpyOscSender();
        var manager = new CueManager(stubEngine, spySender);

        // トリガー20秒 + オフセット+5秒 → 実効25秒
        var cue = CreateCue("c1");
        cue.TriggerTime = new TimecodeValue(0, 0, 20, 0, FrameRate.Fps30);
        cue.TriggerOffset = new TimecodeOffset(false, 0, 0, 5, 0, FrameRate.Fps30);
        cue.SendTimecode = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30);
        manager.AddCue(cue);

        // 現在27秒 → 実効25秒から2秒経過 → 3602秒
        stubEngine.SetCurrentOffsetTimecode(new TimecodeValue(0, 0, 27, 0, FrameRate.Fps30));
        manager.SendCueSync("/cuesync", ["host1"]);

        var call = Assert.Single(spySender.SendCalls);
        Assert.Equal(3602f, Assert.IsType<OscFloat32Argument>(call.Arguments[0]).Value);
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
        public LtcSignalCounts LtcSignalCounts { get; set; }
        public bool LtcAutoRecoverOnSignalLoss { get; set; } = true;

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
