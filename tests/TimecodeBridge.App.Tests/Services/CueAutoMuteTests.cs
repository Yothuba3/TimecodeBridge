using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.App.Tests.Services;

/// <summary>発火時オートミュートと時限解除の動作</summary>
public class CueAutoMuteTests
{
    private sealed class StubEngine : ITimecodeEngine
    {
        public TimecodeValue CurrentRawTimecode => new(0, 0, 0, 0, FrameRate.Fps30);
        public TimecodeValue CurrentOffsetTimecode => new(0, 0, 0, 0, FrameRate.Fps30);
        public TimecodeOffset Offset { get; set; } = TimecodeOffset.Zero(FrameRate.Fps30);
        public FrameRate FrameRate { get; set; } = FrameRate.Fps30;
        public TimecodeSourceType ActiveSource => TimecodeSourceType.Ltc;
        public bool IsReceiving => false;
        public double FreerunDurationSeconds { get; set; }
        public bool IsFreerunning => false;
        public event EventHandler<TimecodeUpdatedEventArgs>? TimecodeUpdated;
        public event EventHandler<TimecodeStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<AudioSamplesEventArgs>? AudioSamplesAvailable;
        public void StartLtc(string audioDeviceId, bool isLoopback = false) { }
        public void StartGenerator(GeneratorSettings settings) { }
        public void ResumeGenerator() { }
        public void ResetGenerator() { }
        public void ResetGenerator(TimecodeValue startTime) { }
        public void StopGenerator() { }
        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class StubSender : IOscSender
    {
        public event EventHandler<OscSendResultEventArgs>? SendCompleted;
        public void Send(string oscAddress, IReadOnlyList<OscArgument> arguments, IReadOnlyList<string> targetHostIds) { }
        public void SendPing(string hostId) { }
        public Task SendIcmpPingAsync(string hostId, int framesPerSecond) => Task.CompletedTask;
    }

    private static Cue MakeCue(bool autoMute, TimecodeValue? unmuteAfter = null) => new()
    {
        Id = "c1",
        Name = "Cue",
        TriggerTime = new TimecodeValue(0, 0, 1, 0, FrameRate.Fps30),
        OscAddress = "/cue",
        AutoMuteOnFire = autoMute,
        AutoUnmuteAfter = unmuteAfter,
    };

    private static CueManager MakeManager(Cue cue)
    {
        var manager = new CueManager(new StubEngine(), new StubSender());
        manager.AddCue(cue);
        return manager;
    }

    [Fact]
    public void 発火時ミュート設定のキューを発火するとミュートされる()
    {
        var manager = MakeManager(MakeCue(autoMute: true));
        var muteEvents = 0;
        manager.MuteStateChanged += (_, _) => Interlocked.Increment(ref muteEvents);

        manager.ManualTrigger("c1");

        Assert.True(manager.IsMuted);
        Assert.Equal(1, muteEvents);
    }

    [Fact]
    public void 設定のないキューではミュートされない()
    {
        var manager = MakeManager(MakeCue(autoMute: false));

        manager.ManualTrigger("c1");

        Assert.False(manager.IsMuted);
    }

    [Fact]
    public void マスタースイッチがOFFならミュートされない()
    {
        var manager = MakeManager(MakeCue(autoMute: true));
        manager.IsAutoMuteEnabled = false;

        manager.ManualTrigger("c1");

        Assert.False(manager.IsMuted);
    }

    [Fact]
    public void 指定時間後にミュートが自動解除される()
    {
        // 5フレーム@30fps ≒ 167ms
        var manager = MakeManager(MakeCue(autoMute: true, new TimecodeValue(0, 0, 0, 5, FrameRate.Fps30)));

        manager.ManualTrigger("c1");
        Assert.True(manager.IsMuted);

        Assert.True(SpinWait.SpinUntil(() => !manager.IsMuted, TimeSpan.FromSeconds(3)),
            "自動解除されなかった");
    }

    [Fact]
    public void 解除時間未指定なら手動解除までミュートを維持する()
    {
        var manager = MakeManager(MakeCue(autoMute: true, unmuteAfter: null));

        manager.ManualTrigger("c1");
        Thread.Sleep(300);

        Assert.True(manager.IsMuted);

        manager.IsMuted = false;
        Assert.False(manager.IsMuted);
    }

    [Fact]
    public void オートミュート中は原因キューと解除予定が公開される()
    {
        var manager = MakeManager(MakeCue(autoMute: true, new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30)));

        Assert.Null(manager.AutoMutedCueId);
        manager.ManualTrigger("c1");

        Assert.Equal("c1", manager.AutoMutedCueId);
        Assert.NotNull(manager.AutoUnmuteAt);
        Assert.True(manager.AutoUnmuteAt > DateTime.UtcNow);
    }

    [Fact]
    public void 解除時間未指定のオートミュートでは解除予定はnull()
    {
        var manager = MakeManager(MakeCue(autoMute: true, unmuteAfter: null));

        manager.ManualTrigger("c1");

        Assert.Equal("c1", manager.AutoMutedCueId);
        Assert.Null(manager.AutoUnmuteAt);
    }

    [Fact]
    public void 自動解除や手動切替で公開状況はクリアされる()
    {
        var manager = MakeManager(MakeCue(autoMute: true, new TimecodeValue(0, 0, 0, 5, FrameRate.Fps30)));

        manager.ManualTrigger("c1");
        Assert.True(SpinWait.SpinUntil(() => !manager.IsMuted, TimeSpan.FromSeconds(3)));
        Assert.Null(manager.AutoMutedCueId);
        Assert.Null(manager.AutoUnmuteAt);

        manager.ManualTrigger("c1");
        manager.IsMuted = false; // 手動解除
        Assert.Null(manager.AutoMutedCueId);
        Assert.Null(manager.AutoUnmuteAt);
    }

    [Fact]
    public void 手動でミュートを切り替えると予約済みの自動解除は破棄される()
    {
        var manager = MakeManager(MakeCue(autoMute: true, new TimecodeValue(0, 0, 0, 5, FrameRate.Fps30)));

        manager.ManualTrigger("c1");
        // 手動で一度解除→再ミュート（自動解除の予約は消えるべき）
        manager.IsMuted = false;
        manager.IsMuted = true;

        Thread.Sleep(600);
        Assert.True(manager.IsMuted);
    }
}
