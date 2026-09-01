using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
namespace TimecodeBridge.Tests.Integration;

using TimecodeBridge.Core.Models;
using TimecodeBridge.Services;
using TimecodeBridge.Services.Interfaces;

public class RelayFlowTests : IDisposable
{
    private readonly TimecodeEngine _engine;
    private readonly HostRegistry _hostRegistry;
    private readonly SpyOscTransport _spyTransport;
    private readonly OscSender _oscSender;
    private readonly TimecodeRelay _relay;

    public RelayFlowTests()
    {
        _engine = new TimecodeEngine(FrameRate.Fps30);
        _hostRegistry = new HostRegistry();
        _spyTransport = new SpyOscTransport();
        _oscSender = new OscSender(_hostRegistry, _spyTransport);
        _relay = new TimecodeRelay(_engine, _oscSender);

        // Register a test host
        _hostRegistry.AddHost(new OscHost
        {
            Id = "host1",
            Name = "Relay Host",
            IpAddress = "192.168.1.100",
            Port = 8000,
            IsEnabled = true,
        });

        // Configure relay
        _relay.OscAddressPattern = "/timecode";
        _relay.TargetHostIds = ["host1"];
        _relay.ContinuousInterval = new RelayInterval(RelayIntervalMode.EveryFrame, 0);
    }

    public void Dispose()
    {
        _engine.Dispose();
    }

    [Fact]
    public async Task RelayFlow_ContinuousSend()
    {
        // Arrange
        _relay.IsContinuousEnabled = true;

        var sendReceived = new TaskCompletionSource<bool>();
        _oscSender.SendCompleted += (_, args) =>
        {
            if (args.Success)
                sendReceived.TrySetResult(true);
        };

        // Act: write a frame
        var tc = new TimecodeValue(0, 1, 30, 15, FrameRate.Fps30);
        _engine.WriteFrame(tc);

        await WaitWithTimeout(sendReceived.Task, TimeSpan.FromSeconds(3));

        // Assert
        Assert.True(_spyTransport.SendCalls.Count >= 1);
        var call = _spyTransport.SendCalls[0];
        Assert.Equal("192.168.1.100", call.IpAddress);
        Assert.Equal(8000, call.Port);
        Assert.Equal("/timecode", call.OscAddress);
        Assert.Single(call.Arguments);
        var arg = Assert.IsType<OscFloat32Argument>(call.Arguments[0]);
        Assert.Equal(tc.TotalFrames() / (float)tc.FrameRate.FramesPerSecond(), arg.Value);
    }

    [Fact]
    public void RelayFlow_OneShotSend()
    {
        // Arrange: continuous disabled, but one-shot should still work
        _relay.IsContinuousEnabled = false;

        // Write a frame to establish current timecode (synchronous setup is not enough
        // since WriteFrame goes through channel; we use a workaround)
        // For one-shot, TimecodeRelay reads _timecodeEngine.CurrentOffsetTimecode
        // which is updated by the worker thread. We need to ensure it's processed.
        var tc = new TimecodeValue(0, 5, 0, 0, FrameRate.Fps30);
        _engine.WriteFrame(tc);
        // Wait for the worker thread to process the frame
        Thread.Sleep(200);

        // Act
        _relay.TriggerOneShot();

        // Assert: exactly 1 send (the one-shot, not continuous)
        Assert.Single(_spyTransport.SendCalls);
        var call = _spyTransport.SendCalls[0];
        Assert.Equal("/timecode", call.OscAddress);
        var arg = Assert.IsType<OscFloat32Argument>(call.Arguments[0]);
        Assert.Equal(tc.TotalFrames() / (float)tc.FrameRate.FramesPerSecond(), arg.Value);
    }

    [Fact]
    public async Task RelayFlow_SignalLossStopsSending()
    {
        // Arrange
        _relay.IsContinuousEnabled = true;

        var lostNotification = new TaskCompletionSource<bool>();
        _oscSender.SendCompleted += (_, args) =>
        {
            if (args.OscAddress == "/timecode/lost")
                lostNotification.TrySetResult(true);
        };

        // Act: write a frame to start receiving
        _engine.WriteFrame(new TimecodeValue(0, 0, 1, 0, FrameRate.Fps30));
        await Task.Delay(100);

        // Wait for signal loss timeout (500ms in TimecodeEngine)
        // The StatusChanged event fires after 500ms with no new frames
        await WaitWithTimeout(lostNotification.Task, TimeSpan.FromSeconds(3));

        // Assert: should have the timecode send + the /lost notification
        Assert.Contains(_spyTransport.SendCalls, c => c.OscAddress == "/timecode");
        Assert.Contains(_spyTransport.SendCalls, c => c.OscAddress == "/timecode/lost");
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
