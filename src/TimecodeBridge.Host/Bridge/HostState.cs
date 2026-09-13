using System.Globalization;
using Avalonia.Threading;
using TimecodeBridge.Core.Audio;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.Host.Services;

namespace TimecodeBridge.Host.Bridge;

[Flags]
public enum Domain
{
    None = 0,
    Project = 1 << 0,
    Mode = 1 << 1,
    Transport = 1 << 2,
    Receive = 1 << 3,
    Generator = 1 << 4,
    NextCue = 1 << 5,
    Cues = 1 << 6,
    Hosts = 1 << 7,
    CueSync = 1 << 8,
    TriggerPanel = 1 << 9,
    All = (1 << 10) - 1,
}

/// <summary>
/// Host が唯一の正として持つアプリ状態と、それを protocol の DTO に写す責務。
/// 変更はドメイン単位の dirty フラグで集約し、UI スレッド上で revision を進めて patch として通知する。
/// </summary>
public sealed class HostState : IDisposable
{
    private const int MaxLogs = 500;

    private readonly ITimecodeEngine _engine;
    private readonly ICueManager _cues;
    private readonly IHostRegistry _hosts;
    private readonly IOscSender _osc;
    private readonly IOscTriggerPanelManager _panel;
    private readonly IProjectService _project;
    private readonly IAudioDeviceService _devices;
    private readonly RecentProjectsStore _recent;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    private readonly Dictionary<string, (DateTime? LastTriggeredUtc, long FlashToken)> _cueRuntime = new();
    private readonly Dictionary<string, string> _hostReachability = new();
    private readonly List<LogDto> _logs = new();
    private readonly List<LogDto> _pendingLogAppend = new();
    private bool _logsReset;
    private Domain _dirty;
    private bool _flushScheduled;
    private TimecodeUpdatedEventArgs? _lastUpdate;
    private string? _lastNextCueId;
    private TimecodeReceiveStatus _receiveStatus = TimecodeReceiveStatus.NotReceiving;
    private string? _lastError;

    public HostState(
        ITimecodeEngine engine, ICueManager cues, IHostRegistry hosts, IOscSender osc,
        IOscTriggerPanelManager panel, IProjectService project, IAudioDeviceService devices,
        RecentProjectsStore recent)
    {
        _engine = engine; _cues = cues; _hosts = hosts; _osc = osc;
        _panel = panel; _project = project; _devices = devices; _recent = recent;
        _recent.Changed += () => MarkDirty(Domain.Project);

        _engine.TimecodeUpdated += OnTimecodeUpdated;
        _engine.StatusChanged += OnStatusChanged;
        _engine.AudioSamplesAvailable += OnAudioSamples;
        if (_engine is TimecodeEngine concrete) concrete.AudioErrorOccurred += OnAudioError;
        _cues.CueTriggered += OnCueTriggered;
        _cues.MuteStateChanged += (_, _) => MarkDirty(Domain.Transport | Domain.Cues);
        _hosts.HostChanged += (_, _) => MarkDirty(Domain.Hosts);
        _panel.Changed += (_, _) => MarkDirty(Domain.TriggerPanel);
        _osc.SendCompleted += OnOscSendCompleted;
        _project.UnsavedChangesStatusChanged += (_, _) => MarkDirty(Domain.Project);
    }

    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public long Revision { get; private set; }
    public string Mode { get; set; } = "ltc";
    public string? SelectedInputDeviceId { get; set; }
    public bool LtcStarted { get; set; }
    public bool GeneratorRunning { get; set; }
    public GeneratorSettings Generator { get; } = new();
    public CueSyncSettings CueSync { get; } = new();
    public WaveReducer Wave { get; } = new();

    /// <summary>Undo/Redo の可否。ProjectCoordinator が設定する。</summary>
    public IProjectHistory? History { get; set; }

    /// <summary>UI スレッド上で発火。(changes, revision, baseRevision)</summary>
    public event Action<StateChanges, long, long>? Changed;

    // ---- dirty / flush ---------------------------------------------------------

    public void MarkDirty(Domain domains)
    {
        if (Dispatcher.UIThread.CheckAccess()) MarkDirtyCore(domains);
        else Dispatcher.UIThread.Post(() => MarkDirtyCore(domains));
    }

    private void MarkDirtyCore(Domain domains)
    {
        _dirty |= domains;
        ScheduleFlush();
    }

    private void ScheduleFlush()
    {
        if (_flushScheduled) return;
        _flushScheduled = true;
        Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
    }

    public void Flush()
    {
        _flushScheduled = false;
        if (_dirty == Domain.None && _pendingLogAppend.Count == 0 && !_logsReset) return;

        var d = _dirty;
        _dirty = Domain.None;
        var changes = new StateChanges(
            Project: d.HasFlag(Domain.Project) ? BuildProject() : null,
            Mode: d.HasFlag(Domain.Mode) ? Mode : null,
            Transport: d.HasFlag(Domain.Transport) ? BuildTransport() : null,
            Receive: d.HasFlag(Domain.Receive) ? BuildReceive() : null,
            Generator: d.HasFlag(Domain.Generator) ? BuildGenerator() : null,
            NextCue: (d & (Domain.NextCue | Domain.Cues)) != 0 ? BuildNextCue() : null,
            Cues: d.HasFlag(Domain.Cues) ? BuildCues() : null,
            Hosts: d.HasFlag(Domain.Hosts) ? BuildHosts() : null,
            CueSync: d.HasFlag(Domain.CueSync) ? BuildCueSync() : null,
            TriggerPanel: d.HasFlag(Domain.TriggerPanel) ? BuildTriggerPanel() : null,
            LogsAppend: !_logsReset && _pendingLogAppend.Count > 0 ? _pendingLogAppend.ToArray() : null,
            LogsReset: _logsReset ? _logs.ToArray() : null);
        _pendingLogAppend.Clear();
        _logsReset = false;

        long baseRevision = Revision;
        Revision++;
        Changed?.Invoke(changes, Revision, baseRevision);
    }

    // ---- snapshot ------------------------------------------------------------------

    public AppState BuildSnapshot() => new(
        SessionId,
        BuildProject(),
        Mode,
        BuildTransport(),
        BuildReceive(),
        BuildGenerator(),
        BuildClock(),
        BuildNextCue(),
        BuildCues(),
        BuildHosts(),
        BuildCueSync(),
        BuildTriggerPanel(),
        _logs.ToArray(),
        new UiCapabilities(true, true, OperatingSystem.IsWindows() ? "windows" : "macos"));

    private ProjectState BuildProject()
    {
        var path = _project.CurrentFilePath;
        var name = path is null ? "無題" : Path.GetFileNameWithoutExtension(path);
        return new ProjectState(name, path, _project.HasUnsavedChanges, History?.CanUndo ?? false, History?.CanRedo ?? false, _recent.Items.ToArray());
    }

    private TransportState BuildTransport()
    {
        string status, text, detail = "";
        if (_lastError is not null)
        {
            (status, text, detail) = (TransportStatus.Error, "エラー", _lastError);
        }
        else if (Mode == "generate")
        {
            (status, text) = GeneratorRunning ? (TransportStatus.Receiving, "内部生成中") : (TransportStatus.Stopped, "停止中");
        }
        else if (!LtcStarted)
        {
            (status, text) = (TransportStatus.Stopped, "停止中");
        }
        else
        {
            (status, text) = _receiveStatus switch
            {
                TimecodeReceiveStatus.Receiving => (TransportStatus.Receiving, "受信中"),
                TimecodeReceiveStatus.Freerunning => (TransportStatus.Freerun, "フリーラン"),
                _ => (TransportStatus.SignalLost, "信号なし"),
            };
            detail = DeviceName(SelectedInputDeviceId) ?? "";
        }

        var counts = _engine.LtcSignalCounts;
        double? errorRate = counts.Written > 0 ? 100.0 * (counts.Written - counts.Accepted) / counts.Written : null;
        return new TransportState(
            status, text, detail, errorRate,
            Locked: _engine.IsReceiving,
            LevelVpp: null,
            TriggerMuted: _cues.IsMuted,
            AutoMuteEnabled: _cues.IsAutoMuteEnabled,
            AutoMutedCueId: _cues.AutoMutedCueId,
            AutoUnmuteAtUtc: _cues.AutoUnmuteAt?.ToUniversalTime().ToString("O"));
    }

    private ReceiveState BuildReceive() => new(
        SelectedInputDeviceId,
        _devices.GetCaptureDevices().Concat(_devices.GetRenderDevices()).Select(ToDto).ToArray(),
        _engine.Offset.ToString(),
        _cues.TriggerWindowFrames,
        _engine.FreerunDurationSeconds);

    private GeneratorState BuildGenerator() => new(
        GeneratorRunning,
        Generator.StartTime.ToString(),
        ToCode(Generator.FrameRate),
        string.IsNullOrEmpty(Generator.OutputDeviceId) ? null : Generator.OutputDeviceId,
        _devices.GetRenderDevices().Where(d => !d.IsLoopback).Select(ToDto).ToArray(),
        Generator.VolumeLevel,
        LtcOutputActive: GeneratorRunning && !string.IsNullOrEmpty(Generator.OutputDeviceId),
        SettingsPendingReset: false);

    public ClockState BuildClock()
    {
        var update = _lastUpdate;
        var raw = update?.RawTimecode ?? _engine.CurrentRawTimecode;
        var display = update?.OffsetTimecode ?? _engine.CurrentOffsetTimecode;
        var next = FindNextCue(display);
        if (next?.Id != _lastNextCueId)
        {
            _lastNextCueId = next?.Id;
            MarkDirty(Domain.NextCue);
        }
        return new ClockState(
            raw.ToString(), display.ToString(), display.TotalFrames(), ToCode(display.FrameRate), display.FrameRate.IsDropFrame(),
            _clock.Elapsed.TotalMilliseconds,
            next?.Id, next is null ? null : next.GetEffectiveTriggerTime().ToOrdinal() - display.ToOrdinal());
    }

    public WaveState? TryBuildWave(int points)
    {
        var snap = Wave.Snapshot(points);
        if (snap is null) return null;
        int rate = _engine is TimecodeEngine e ? e.CaptureSampleRate : 48000;
        return new WaveState(rate, Wave.WindowSamples * 1000.0 / Math.Max(1, rate), snap.Value.Min, snap.Value.Max, snap.Value.LevelDbfs);
    }

    private NextCueState? BuildNextCue()
    {
        var current = _lastUpdate?.OffsetTimecode ?? _engine.CurrentOffsetTimecode;
        var cue = FindNextCue(current);
        if (cue is null) return null;
        var effective = cue.GetEffectiveTriggerTime();
        return new NextCueState(cue.Id, cue.Name, cue.TriggerTime.ToString(), effective.ToString(), effective.ToOrdinal() - current.ToOrdinal());
    }

    private Cue? FindNextCue(TimecodeValue current)
    {
        long now = current.ToOrdinal();
        Cue? best = null; long bestOrd = long.MaxValue;
        foreach (var cue in _cues.Cues)
        {
            if (!cue.IsEnabled) continue;
            long ord = cue.GetEffectiveTriggerTime().ToOrdinal();
            if (ord >= now && ord < bestOrd) { best = cue; bestOrd = ord; }
        }
        return best;
    }

    private IReadOnlyList<CueDto> BuildCues()
    {
        var now = DateTime.UtcNow;
        var result = new List<CueDto>(_cues.Cues.Count);
        foreach (var c in _cues.Cues)
        {
            _cueRuntime.TryGetValue(c.Id, out var rt);
            string countdown = "";
            if (_cues.AutoMutedCueId == c.Id && _cues.AutoUnmuteAt is { } until)
            {
                var remaining = until.ToUniversalTime() - now;
                if (remaining > TimeSpan.Zero) countdown = remaining.ToString(@"m\:ss", CultureInfo.InvariantCulture);
            }
            result.Add(new CueDto(
                c.Id, c.Name, c.Memo, c.TriggerTime.ToString(), c.GetEffectiveTriggerTime().ToString(), ToCode(c.TriggerTime.FrameRate),
                c.OscAddress, c.AdditionalOscAddresses.ToArray(), c.Arguments.Select(ToDto).ToArray(), c.TargetHostIds.ToArray(),
                c.IsEnabled, c.SendTriggerTimeAsSeconds, c.SendTimecode?.ToString(), c.TriggerOffset?.ToString(),
                c.AutoMuteOnFire, c.AutoUnmuteAfter?.ToString(),
                new CueRuntime(rt.LastTriggeredUtc?.ToString("O"), rt.FlashToken, countdown)));
        }
        return result;
    }

    private IReadOnlyList<HostDto> BuildHosts() =>
        _hosts.Hosts.Select(h => new HostDto(h.Id, h.Name, h.IpAddress, h.Port, h.IsEnabled, _hostReachability.GetValueOrDefault(h.Id, "unknown"))).ToArray();

    /// <summary>疎通確認の結果("checking" / "reachable" / "unreachable")を保持し、hosts を更新通知する。</summary>
    public void SetHostReachability(string hostId, string reachability)
    {
        _hostReachability[hostId] = reachability;
        MarkDirty(Domain.Hosts);
    }

    private CueSyncState BuildCueSync() => new(CueSync.OscAddress, CueSync.TargetHostIds.ToArray());

    private TriggerPanelState BuildTriggerPanel() => new(
        _panel.Rows, _panel.Columns,
        _panel.Buttons.Select(b => new TriggerButtonDto(b.Id, b.Row, b.Column, b.Label, b.OscAddress, b.Arguments.Select(ToDto).ToArray(), b.TargetHostIds.ToArray())).ToArray());

    // ---- logs ----------------------------------------------------------------------

    public void AppendLog(string message, bool success)
    {
        var entry = new LogDto(Guid.NewGuid().ToString("N"), DateTime.UtcNow.ToString("O"), message, success);
        _logs.Add(entry);
        if (_logs.Count > MaxLogs) _logs.RemoveRange(0, _logs.Count - MaxLogs);
        _pendingLogAppend.Add(entry);
        ScheduleFlush();
    }

    public void ClearLogs()
    {
        _logs.Clear();
        _pendingLogAppend.Clear();
        _logsReset = true;
        ScheduleFlush();
    }

    public void SetError(string? message)
    {
        _lastError = message;
        MarkDirty(Domain.Transport);
    }

    // ---- engine / service events ------------------------------------------------------

    private void OnTimecodeUpdated(object? sender, TimecodeUpdatedEventArgs e) => _lastUpdate = e;

    private void OnStatusChanged(object? sender, TimecodeStatusChangedEventArgs e)
    {
        _receiveStatus = e.Status;
        MarkDirty(Domain.Transport);
    }

    private void OnAudioSamples(object? sender, AudioSamplesEventArgs e)
    {
        if (_engine is TimecodeEngine concrete) Wave.Configure(concrete.CaptureSampleRate);
        Wave.Write(e.Samples);
    }

    private void OnAudioError(object? sender, AudioErrorEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _lastError = e.Message;
            LtcStarted = false;
            AppendLog($"音声エラー: {e.Message}", false);
            MarkDirty(Domain.Transport);
        });
    }

    private void OnCueTriggered(object? sender, CueTriggeredEventArgs e)
    {
        var id = e.Cue.Id;
        Dispatcher.UIThread.Post(() =>
        {
            _cueRuntime.TryGetValue(id, out var rt);
            _cueRuntime[id] = (DateTime.UtcNow, rt.FlashToken + 1);
            MarkDirty(Domain.Cues);
        });
    }

    private void OnOscSendCompleted(object? sender, OscSendResultEventArgs e)
    {
        var text = e.Success
            ? $"{e.OscAddress} → {e.HostName}"
            : $"{e.OscAddress} → {e.HostName} 失敗: {e.ErrorMessage}";
        if (Dispatcher.UIThread.CheckAccess()) AppendLog(text, e.Success);
        else Dispatcher.UIThread.Post(() => AppendLog(text, e.Success));
    }

    // ---- 変換 ----------------------------------------------------------------------

    public string? DeviceName(string? id) =>
        id is null ? null : _devices.GetCaptureDevices().Concat(_devices.GetRenderDevices()).FirstOrDefault(d => d.Id == id)?.DisplayName;

    public IEnumerable<AudioDeviceInfo> AllDevices() => _devices.GetCaptureDevices().Concat(_devices.GetRenderDevices());

    public AudioDeviceInfo? FindDevice(string? id) =>
        id is null ? null : _devices.GetCaptureDevices().Concat(_devices.GetRenderDevices()).FirstOrDefault(d => d.Id == id);

    private static AudioDevice ToDto(AudioDeviceInfo d) => new(d.Id, d.DisplayName, d.IsLoopback);

    public static OscArgumentDto ToDto(OscArgument a) => a switch
    {
        OscInt32Argument i => OscArgumentDto.Int32(i.Value),
        OscFloat32Argument f => OscArgumentDto.Float32(f.Value),
        OscStringArgument s => OscArgumentDto.String(s.Value),
        _ => OscArgumentDto.String(a.ToString() ?? ""),
    };

    public static string ToCode(FrameRate rate) => rate switch
    {
        FrameRate.Fps24 => FrameRateCode.Fps24,
        FrameRate.Fps25 => FrameRateCode.Fps25,
        FrameRate.Fps2997Drop => FrameRateCode.Fps2997Drop,
        _ => FrameRateCode.Fps30,
    };

    public static bool TryParseFrameRate(string? code, out FrameRate rate)
    {
        switch (code)
        {
            case FrameRateCode.Fps24: rate = FrameRate.Fps24; return true;
            case FrameRateCode.Fps25: rate = FrameRate.Fps25; return true;
            case FrameRateCode.Fps2997Drop: rate = FrameRate.Fps2997Drop; return true;
            case FrameRateCode.Fps30: rate = FrameRate.Fps30; return true;
            default: rate = FrameRate.Fps30; return false;
        }
    }

    /// <summary>全角の数字・コロン・符号を半角へ寄せる。UI は半角しか受け付けないが、貼り付けや旧データの保険。</summary>
    public static string NormalizeTimecodeText(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text.Trim())
        {
            sb.Append(ch switch
            {
                >= '０' and <= '９' => (char)('0' + (ch - '０')),
                '：' => ':',
                '；' => ';',
                '＋' => '+',
                '－' or '−' or 'ー' => '-',
                _ => ch,
            });
        }
        return sb.ToString();
    }

    /// <summary>"HH:MM:SS:FF"(ドロップフレームは最後の区切りが ';' でもよい)を解釈する。</summary>
    public static bool TryParseTimecode(string? text, FrameRate rate, out TimecodeValue value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = NormalizeTimecodeText(text).Split(':', ';');
        if (parts.Length != 4) return false;
        var n = new int[4];
        for (int i = 0; i < 4; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out n[i])) return false;
        if (n[0] > 23 || n[1] > 59 || n[2] > 59 || n[3] >= rate.FramesPerSecond()) return false;
        value = new TimecodeValue(n[0], n[1], n[2], n[3], rate);
        return true;
    }

    public void Dispose()
    {
        _engine.TimecodeUpdated -= OnTimecodeUpdated;
        _engine.StatusChanged -= OnStatusChanged;
        _engine.AudioSamplesAvailable -= OnAudioSamples;
        if (_engine is TimecodeEngine concrete) concrete.AudioErrorOccurred -= OnAudioError;
        _cues.CueTriggered -= OnCueTriggered;
        _osc.SendCompleted -= OnOscSendCompleted;
    }
}
