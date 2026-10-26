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
    public void ApplyGeneratorOutput(GeneratorSettings settings) => Calls.Add($"ApplyGeneratorOutput:{settings.OutputDeviceId}");
    public void SetGeneratorVolume(float level) => Calls.Add($"SetGeneratorVolume:{level}");
    public void Stop() => Calls.Add("Stop");

    public void RaiseTimecode(TimecodeValue raw, TimecodeValue offset) => TimecodeUpdated?.Invoke(this, new TimecodeUpdatedEventArgs(raw, offset));
    public void RaiseStatus(TimecodeReceiveStatus status) => StatusChanged?.Invoke(this, new TimecodeStatusChangedEventArgs(status));
    public void RaiseSamples(float[] samples) => AudioSamplesAvailable?.Invoke(this, new AudioSamplesEventArgs(samples));
}

public sealed class FakeOscSender : IOscSender
{
    public IHostRegistry? HostRegistry { get; set; }
    public List<(string Address, IReadOnlyList<string> Hosts)> Sent { get; } = new();
    public List<string> Pinged { get; } = new();
    public event EventHandler<OscSendResultEventArgs>? SendCompleted;

    public void Send(string oscAddress, IReadOnlyList<OscArgument> arguments, IReadOnlyList<string> targetHostIds) => Sent.Add((oscAddress, targetHostIds));
    public OscDispatchResult SendWithResult(string oscAddress, IReadOnlyList<OscArgument> arguments, IReadOnlyList<string> targetHostIds)
    {
        var enabled = HostRegistry is null ? targetHostIds.Distinct().ToList() : HostRegistry.GetEnabledHosts(targetHostIds).Select(h => h.Id).ToList();
        var skipped = targetHostIds.Distinct().Except(enabled).ToList();
        if (enabled.Count > 0) Sent.Add((oscAddress, enabled));
        else
        {
            var disabled = HostRegistry?.Hosts.Where(h => skipped.Contains(h.Id) && !h.IsEnabled).Select(h => h.Name).ToList() ?? [];
            var missing = skipped.Where(id => HostRegistry?.Hosts.All(h => h.Id != id) != false).ToList();
            var details = new List<string>();
            if (disabled.Count > 0) details.Add($"無効: {string.Join(", ", disabled)}");
            if (missing.Count > 0) details.Add($"見つからない: {string.Join(", ", missing)}");
            SendCompleted?.Invoke(this, new OscSendResultEventArgs
            {
                OscAddress = oscAddress, HostId = "", HostName = "", Success = false,
                ErrorMessage = details.Count == 0 ? "送信先がありません" : $"送信先がありません({string.Join(", ", details)})",
            });
        }
        return new OscDispatchResult(enabled.Count, skipped);
    }
    public void SendPing(string hostId) => Pinged.Add(hostId);
    public Task SendIcmpPingAsync(string hostId, int framesPerSecond) => Task.CompletedTask;
    public void Complete(string address, string host, bool success, string? error = null) =>
        SendCompleted?.Invoke(this, new OscSendResultEventArgs { OscAddress = address, HostId = host, HostName = host, Success = success, ErrorMessage = error });
}

public sealed class FakeDeviceService : IAudioDeviceService
{
    // IAudioDeviceService の契約どおり、ループバック取り込みはキャプチャ側に並ぶ(WindowsAudioDeviceService と同じ)
    public List<AudioDeviceInfo> Capture { get; } = new() { new("in-1", "Audio Bridge 2-A", false), new("loop-1", "Loopback", true) };
    public List<AudioDeviceInfo> Render { get; } = new() { new("out-1", "Speakers", false) };
    /// <summary>列挙した回数(実機では 1 回が重い)</summary>
    public int Enumerations { get; private set; }
    /// <summary>設定すると、列挙はこれが開くまで終わらない(実機の遅い列挙の代わり)</summary>
    public ManualResetEventSlim? Gate { get; set; }
    // 実機の列挙と同じく、その時点の写しを返す
    public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices() { Gate?.Wait(); Enumerations++; return Capture.ToArray(); }
    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices() { Gate?.Wait(); Enumerations++; return Render.ToArray(); }
}
