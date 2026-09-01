using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
namespace TimecodeBridge.Tests.ViewModels;

using TimecodeBridge.Core.Models;
using TimecodeBridge.Services.Interfaces;
using TimecodeBridge.ViewModels;

// --- Stub ---

internal class StubOscSenderForLog : IOscSender
{
    public event EventHandler<OscSendResultEventArgs>? SendCompleted;

    public void Send(string oscAddress, IReadOnlyList<OscArgument> arguments, IReadOnlyList<string> targetHostIds) { }
    public void SendPing(string hostId) { }
    public Task SendIcmpPingAsync(string hostId, int framesPerSecond) => Task.CompletedTask;

    public void RaiseSendCompleted(OscSendResultEventArgs args)
    {
        SendCompleted?.Invoke(this, args);
    }
}

// --- Tests ---

public class LogViewModelTests
{
    private readonly StubOscSenderForLog _oscSender = new();

    private LogViewModel CreateVm() => new(_oscSender);

    private static OscSendResultEventArgs CreateSuccessResult(string address = "/test", string hostId = "h1", string hostName = "Host1")
    {
        return new OscSendResultEventArgs
        {
            OscAddress = address,
            HostId = hostId,
            HostName = hostName,
            Success = true,
        };
    }

    private static OscSendResultEventArgs CreateFailureResult(string address = "/test", string hostId = "h1", string hostName = "Host1", string error = "Timeout")
    {
        return new OscSendResultEventArgs
        {
            OscAddress = address,
            HostId = hostId,
            HostName = hostName,
            Success = false,
            ErrorMessage = error,
        };
    }

    // --- Initial state ---

    [Fact]
    public void Constructor_InitializesEmptyLogs()
    {
        var vm = CreateVm();
        Assert.NotNull(vm.Logs);
        Assert.Empty(vm.Logs);
    }

    // --- SendCompleted adds log entries ---

    [Fact]
    public void SendCompleted_Success_AddsLogEntry()
    {
        var vm = CreateVm();

        _oscSender.RaiseSendCompleted(CreateSuccessResult());

        Assert.Single(vm.Logs);
        Assert.True(vm.Logs[0].IsSuccess);
    }

    [Fact]
    public void SendCompleted_Failure_AddsLogEntry()
    {
        var vm = CreateVm();

        _oscSender.RaiseSendCompleted(CreateFailureResult());

        Assert.Single(vm.Logs);
        Assert.False(vm.Logs[0].IsSuccess);
        Assert.Contains("Timeout", vm.Logs[0].Message);
    }

    [Fact]
    public void SendCompleted_LogEntryContainsTimestamp()
    {
        var vm = CreateVm();
        var before = DateTime.Now;

        _oscSender.RaiseSendCompleted(CreateSuccessResult());

        var after = DateTime.Now;
        Assert.InRange(vm.Logs[0].Timestamp, before, after);
    }

    [Fact]
    public void SendCompleted_LogEntryContainsHostAndAddress()
    {
        var vm = CreateVm();

        _oscSender.RaiseSendCompleted(CreateSuccessResult("/timecode", "h1", "MyHost"));

        Assert.Contains("/timecode", vm.Logs[0].Message);
        Assert.Contains("MyHost", vm.Logs[0].Message);
    }

    // --- Circular buffer ---

    [Fact]
    public void CircularBuffer_DoesNotExceed1000Entries()
    {
        var vm = CreateVm();

        for (int i = 0; i < 1005; i++)
        {
            _oscSender.RaiseSendCompleted(CreateSuccessResult($"/addr{i}", $"h{i}", $"Host{i}"));
        }

        Assert.Equal(1000, vm.Logs.Count);
    }

    [Fact]
    public void CircularBuffer_RemovesOldestFirst()
    {
        var vm = CreateVm();

        for (int i = 0; i < 1005; i++)
        {
            _oscSender.RaiseSendCompleted(CreateSuccessResult($"/addr{i}", $"h{i}", $"Host{i}"));
        }

        // The oldest entries (0-4) should have been removed
        // The first remaining entry should be from index 5
        Assert.Contains("/addr5", vm.Logs[0].Message);
    }

    // --- ClearLogsCommand ---

    [Fact]
    public void ClearLogsCommand_ClearsAllLogs()
    {
        var vm = CreateVm();

        _oscSender.RaiseSendCompleted(CreateSuccessResult());
        _oscSender.RaiseSendCompleted(CreateFailureResult());

        vm.ClearLogsCommand.Execute(null);

        Assert.Empty(vm.Logs);
    }

    [Fact]
    public void ClearLogsCommand_OnEmptyLogs_NoError()
    {
        var vm = CreateVm();

        vm.ClearLogsCommand.Execute(null);

        Assert.Empty(vm.Logs);
    }
}
