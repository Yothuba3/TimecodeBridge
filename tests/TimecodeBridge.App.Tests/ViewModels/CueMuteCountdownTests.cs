using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.App.ViewModels;

namespace TimecodeBridge.App.Tests.ViewModels;

/// <summary>キューリスト行のミュート解除カウントダウン表示</summary>
public class CueMuteCountdownTests
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
        public LtcSignalCounts LtcSignalCounts { get; set; }
        public bool LtcAutoRecoverOnSignalLoss { get; set; } = true;
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

    private sealed class StubHostRegistry : IHostRegistry
    {
        public IReadOnlyList<OscHost> Hosts => [];
        public event EventHandler<HostChangedEventArgs>? HostChanged;
        public void AddHost(OscHost host) { }
        public void UpdateHost(string hostId, OscHost updatedHost) { }
        public void RemoveHost(string hostId) { }
        public void SetHostEnabled(string hostId, bool enabled) { }
        public IReadOnlyList<OscHost> GetEnabledHosts(IReadOnlyList<string> hostIds) => [];
    }

    private sealed class StubCueDialogService : ICueDialogService
    {
        public Cue? ShowEditDialog(Cue template, IReadOnlyList<OscHost> hosts, FrameRate frameRate, string title) => null;
        public CueBatchEditResult? ShowBatchEditDialog(int cueCount, IReadOnlyList<OscHost> hosts, FrameRate frameRate) => null;
        public (int Count, TimeSpan Interval)? ShowBatchDuplicateDialog() => null;
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

    private static (CueManager Manager, CueListViewModel Vm) Create(TimecodeValue? unmuteAfter)
    {
        var manager = new CueManager(new StubEngine(), new StubSender());
        manager.AddCue(new Cue
        {
            Id = "c1",
            Name = "Cue",
            TriggerTime = new TimecodeValue(0, 0, 1, 0, FrameRate.Fps30),
            OscAddress = "/cue",
            AutoMuteOnFire = true,
            AutoUnmuteAfter = unmuteAfter,
        });
        var vm = new CueListViewModel(manager, new StubEngine(), new StubHostRegistry(), new StubCueDialogService(), new StubProjectService());
        return (manager, vm);
    }

    [AvaloniaFact]
    public void 時限解除ありの発火でカウントダウンが表示される()
    {
        var (manager, vm) = Create(new TimecodeValue(0, 0, 30, 0, FrameRate.Fps30));

        manager.ManualTrigger("c1");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("解除まで", vm.CueItems[0].MuteStatusLabel);
        Assert.StartsWith("00:00:2", vm.CueItems[0].MuteCountdownText);
    }

    [AvaloniaFact]
    public void 解除時間未指定の発火ではMUTE中と表示される()
    {
        var (manager, vm) = Create(unmuteAfter: null);

        manager.ManualTrigger("c1");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("MUTE中", vm.CueItems[0].MuteStatusLabel);
        Assert.Equal("", vm.CueItems[0].MuteCountdownText);
    }

    [AvaloniaFact]
    public void 手動でミュートを解除すると表示が消える()
    {
        var (manager, vm) = Create(new TimecodeValue(0, 0, 30, 0, FrameRate.Fps30));

        manager.ManualTrigger("c1");
        Dispatcher.UIThread.RunJobs();
        Assert.NotEqual("", vm.CueItems[0].MuteStatusLabel);

        manager.IsMuted = false;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("", vm.CueItems[0].MuteStatusLabel);
        Assert.Equal("", vm.CueItems[0].MuteCountdownText);
    }
}
