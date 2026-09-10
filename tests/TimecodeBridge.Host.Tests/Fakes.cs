using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Host.Tests;

public sealed class FakeEngine : ITimecodeEngine
{
    public List<string> Calls { get; } = new();
    public TimecodeValue CurrentRawTimecode { get; set; } = new(0, 0, 0, 0, FrameRate.Fps30);
    public TimecodeValue CurrentOffsetTimecode { get; set; } = new(0, 0, 0, 0, FrameRate.Fps30);
    public TimecodeOffset Offset { get; set; } = TimecodeOffset.Zero(FrameRate.Fps30);
    public FrameRate FrameRate { get; set; } = FrameRate.Fps30;
    public TimecodeSourceType ActiveSource { get; set; } = TimecodeSourceType.Ltc;
    public bool IsReceiving { get; set; }
    public double FreerunDurationSeconds { get; set; }
    public bool IsFreerunning { get; set; }
    public LtcSignalCounts LtcSignalCounts { get; set; }

    public event EventHandler<TimecodeUpdatedEventArgs>? TimecodeUpdated;
    public event EventHandler<TimecodeStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<AudioSamplesEventArgs>? AudioSamplesAvailable;

    public void StartLtc(string audioDeviceId, bool isLoopback = false) => Calls.Add($"StartLtc:{audioDeviceId}:{isLoopback}");
    public void StartGenerator(GeneratorSettings settings) => Calls.Add($"StartGenerator:{settings.StartTime}");
    public void ResumeGenerator() => Calls.Add("ResumeGenerator");
    public void ResetGenerator() => Calls.Add("ResetGenerator");
    public void ResetGenerator(TimecodeValue startTime) => Calls.Add($"ResetGenerator:{startTime}");
    public void StopGenerator() => Calls.Add("StopGenerator");
    public void Stop() => Calls.Add("Stop");

    public void RaiseTimecode(TimecodeValue raw, TimecodeValue offset) => TimecodeUpdated?.Invoke(this, new TimecodeUpdatedEventArgs(raw, offset));
    public void RaiseStatus(TimecodeReceiveStatus status) => StatusChanged?.Invoke(this, new TimecodeStatusChangedEventArgs(status));
    public void RaiseSamples(float[] samples) => AudioSamplesAvailable?.Invoke(this, new AudioSamplesEventArgs(samples));
}

public sealed class FakeOscSender : IOscSender
{
    public List<(string Address, IReadOnlyList<string> Hosts)> Sent { get; } = new();
    public List<string> Pinged { get; } = new();
    public event EventHandler<OscSendResultEventArgs>? SendCompleted;

    public void Send(string oscAddress, IReadOnlyList<OscArgument> arguments, IReadOnlyList<string> targetHostIds) => Sent.Add((oscAddress, targetHostIds));
    public void SendPing(string hostId) => Pinged.Add(hostId);
    public Task SendIcmpPingAsync(string hostId, int framesPerSecond) => Task.CompletedTask;
    public void Complete(string address, string host, bool success, string? error = null) =>
        SendCompleted?.Invoke(this, new OscSendResultEventArgs { OscAddress = address, HostId = host, HostName = host, Success = success, ErrorMessage = error });
}

public sealed class FakeDeviceService : IAudioDeviceService
{
    public List<AudioDeviceInfo> Capture { get; } = new() { new("in-1", "Audio Bridge 2-A", false) };
    public List<AudioDeviceInfo> Render { get; } = new() { new("out-1", "Speakers", false), new("loop-1", "Loopback", true) };
    public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices() => Capture;
    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices() => Render;
}
