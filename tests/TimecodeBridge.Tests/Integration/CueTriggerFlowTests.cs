namespace TimecodeBridge.Tests.Integration;

using TimecodeBridge.Models;
using TimecodeBridge.Services;
using TimecodeBridge.Services.Interfaces;

public class CueTriggerFlowTests : IDisposable
{
    private readonly TimecodeEngine _engine;
    private readonly HostRegistry _hostRegistry;
    private readonly SpyOscTransport _spyTransport;
    private readonly OscSender _oscSender;
    private readonly CueManager _cueManager;

    public CueTriggerFlowTests()
    {
        _engine = new TimecodeEngine(FrameRate.Fps30);
        _hostRegistry = new HostRegistry();
        _spyTransport = new SpyOscTransport();
        _oscSender = new OscSender(_hostRegistry, _spyTransport);
        _cueManager = new CueManager(_engine, _oscSender);

        // Register a test host
        _hostRegistry.AddHost(new OscHost
        {
            Id = "host1",
            Name = "Test Host",
            IpAddress = "127.0.0.1",
            Port = 9000,
            IsEnabled = true,
        });
    }

    public void Dispose()
    {
        _engine.Dispose();
    }

    [Fact]
    public async Task CueTriggerFlow_NormalTrigger()
    {
        // Arrange: register a cue at 00:00:10:00
        var cue = new Cue
        {
            Id = "cue-1",
            Name = "Normal Trigger Cue",
            TriggerTime = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30),
            OscAddress = "/cue/fire",
            Arguments = [new OscInt32Argument(1)],
            TargetHostIds = ["host1"],
        };
        _cueManager.AddCue(cue);

        var triggered = new TaskCompletionSource<CueTriggeredEventArgs>();
        _cueManager.CueTriggered += (_, args) => triggered.TrySetResult(args);

        // Act: write frames to simulate timecode progression
        // First frame to establish _lastTimecode
        _engine.WriteFrame(new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30));
        await Task.Delay(100); // Allow worker thread to process

        // Second frame that crosses the cue trigger time
        _engine.WriteFrame(new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30));

        // Wait for the cue to trigger
        var result = await WaitWithTimeout(triggered.Task, TimeSpan.FromSeconds(3));

        // Assert
        Assert.NotNull(result);
        Assert.Equal("cue-1", result.Cue.Id);
        Assert.False(result.IsManual);
        Assert.Single(_spyTransport.SendCalls);
        Assert.Equal("127.0.0.1", _spyTransport.SendCalls[0].IpAddress);
        Assert.Equal(9000, _spyTransport.SendCalls[0].Port);
        Assert.Equal("/cue/fire", _spyTransport.SendCalls[0].OscAddress);
        Assert.Single(_spyTransport.SendCalls[0].Arguments);
        Assert.IsType<OscInt32Argument>(_spyTransport.SendCalls[0].Arguments[0]);
    }

    [Fact]
    public async Task CueTriggerFlow_FrameSkipTrigger()
    {
        // Arrange: register multiple cues
        _cueManager.AddCue(new Cue
        {
            Id = "cue-1",
            Name = "Cue at 3s",
            TriggerTime = new TimecodeValue(0, 0, 3, 0, FrameRate.Fps30),
            OscAddress = "/cue/1",
            TargetHostIds = ["host1"],
        });
        _cueManager.AddCue(new Cue
        {
            Id = "cue-2",
            Name = "Cue at 5s",
            TriggerTime = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30),
            OscAddress = "/cue/2",
            TargetHostIds = ["host1"],
        });
        _cueManager.AddCue(new Cue
        {
            Id = "cue-3",
            Name = "Cue at 8s",
            TriggerTime = new TimecodeValue(0, 0, 8, 0, FrameRate.Fps30),
            OscAddress = "/cue/3",
            TargetHostIds = ["host1"],
        });

        int triggerCount = 0;
        var allTriggered = new TaskCompletionSource<bool>();
        _cueManager.CueTriggered += (_, _) =>
        {
            if (Interlocked.Increment(ref triggerCount) >= 3)
                allTriggered.TrySetResult(true);
        };

        // 判定幅内のスキップは範囲内のキューを全発火させる仕様。
        // 8秒(240フレーム)のジャンプを許容するよう判定幅を広げておく
        _cueManager.TriggerWindowFrames = 300;

        // Act: establish _lastTimecode at 1s
        _engine.WriteFrame(new TimecodeValue(0, 0, 1, 0, FrameRate.Fps30));
        await Task.Delay(100);

        // Frame skip: jump from 1s to 9s -> all 3 cues in range (1s, 9s]
        _engine.WriteFrame(new TimecodeValue(0, 0, 9, 0, FrameRate.Fps30));

        await WaitWithTimeout(allTriggered.Task, TimeSpan.FromSeconds(3));

        // Assert: all 3 cues triggered
        Assert.Equal(3, _spyTransport.SendCalls.Count);
        Assert.Contains(_spyTransport.SendCalls, c => c.OscAddress == "/cue/1");
        Assert.Contains(_spyTransport.SendCalls, c => c.OscAddress == "/cue/2");
        Assert.Contains(_spyTransport.SendCalls, c => c.OscAddress == "/cue/3");
    }

    [Fact]
    public void CueTriggerFlow_ManualTrigger()
    {
        // Arrange
        var cue = new Cue
        {
            Id = "cue-manual",
            Name = "Manual Cue",
            TriggerTime = new TimecodeValue(0, 0, 99, 0, FrameRate.Fps30),
            OscAddress = "/manual/fire",
            Arguments = [new OscStringArgument("go")],
            TargetHostIds = ["host1"],
        };
        _cueManager.AddCue(cue);

        CueTriggeredEventArgs? triggeredArgs = null;
        _cueManager.CueTriggered += (_, args) => triggeredArgs = args;

        // Act
        _cueManager.ManualTrigger("cue-manual");

        // Assert
        Assert.NotNull(triggeredArgs);
        Assert.True(triggeredArgs.IsManual);
        Assert.Equal("cue-manual", triggeredArgs.Cue.Id);
        Assert.Single(_spyTransport.SendCalls);
        Assert.Equal("/manual/fire", _spyTransport.SendCalls[0].OscAddress);
        var arg = Assert.IsType<OscStringArgument>(_spyTransport.SendCalls[0].Arguments[0]);
        Assert.Equal("go", arg.Value);
    }

    [Fact]
    public async Task CueTriggerFlow_DisabledCueSkipped()
    {
        // Arrange: add an enabled cue and a disabled cue at the same trigger time
        _cueManager.AddCue(new Cue
        {
            Id = "cue-enabled",
            Name = "Enabled Cue",
            TriggerTime = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30),
            OscAddress = "/enabled",
            TargetHostIds = ["host1"],
            IsEnabled = true,
        });
        _cueManager.AddCue(new Cue
        {
            Id = "cue-disabled",
            Name = "Disabled Cue",
            TriggerTime = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30),
            OscAddress = "/disabled",
            TargetHostIds = ["host1"],
            IsEnabled = false,
        });

        var triggered = new TaskCompletionSource<bool>();
        _cueManager.CueTriggered += (_, _) => triggered.TrySetResult(true);

        // Act
        _engine.WriteFrame(new TimecodeValue(0, 0, 1, 0, FrameRate.Fps30));
        await Task.Delay(100);

        _engine.WriteFrame(new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30));

        await WaitWithTimeout(triggered.Task, TimeSpan.FromSeconds(3));

        // Assert: only enabled cue triggered
        Assert.Single(_spyTransport.SendCalls);
        Assert.Equal("/enabled", _spyTransport.SendCalls[0].OscAddress);
    }

    private static async Task<T> WaitWithTimeout<T>(Task<T> task, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var completedTask = await Task.WhenAny(task, Task.Delay(timeout, cts.Token));
        if (completedTask == task)
        {
            cts.Cancel();
            return await task;
        }
        throw new TimeoutException("Operation timed out waiting for event.");
    }

    /// <summary>
    /// Spy implementation of IOscTransport that records all Send calls.
    /// </summary>
    private class SpyOscTransport : IOscTransport
    {
        public List<OscTransportSendCall> SendCalls { get; } = [];

        public void Send(string ipAddress, int port, string oscAddress, IReadOnlyList<OscArgument> arguments)
        {
            lock (SendCalls)
            {
                SendCalls.Add(new OscTransportSendCall(ipAddress, port, oscAddress, arguments.ToList().AsReadOnly()));
            }
        }

        public record OscTransportSendCall(
            string IpAddress,
            int Port,
            string OscAddress,
            IReadOnlyList<OscArgument> Arguments);
    }
}
