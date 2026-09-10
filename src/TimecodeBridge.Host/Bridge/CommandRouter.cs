using System.Text.Json;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Host.Bridge;

/// <summary>Web からの command を Core サービスの操作に写す。UI スレッド上で直列に実行される。</summary>
public sealed class CommandRouter
{
    private readonly HostState _state;
    private readonly ITimecodeEngine _engine;
    private readonly ICueManager _cues;
    private readonly IHostRegistry _hosts;
    private readonly IOscSender _osc;
    private readonly ITimecodeRelay _relay;
    private readonly IOscTriggerPanelManager _panel;

    public CommandRouter(HostState state, ITimecodeEngine engine, ICueManager cues, IHostRegistry hosts,
        IOscSender osc, ITimecodeRelay relay, IOscTriggerPanelManager panel)
    {
        _state = state; _engine = engine; _cues = cues; _hosts = hosts; _osc = osc; _relay = relay; _panel = panel;
    }

    public Action? CloseRequested { get; set; }

    public ResultMessage Execute(WebMessage msg)
    {
        var id = msg.RequestId ?? "";
        if (msg.ProtocolVersion != Protocol.Version)
            return Fail(id, ErrorCode.UnsupportedVersion, $"protocolVersion {msg.ProtocolVersion} は未対応です");
        if (string.IsNullOrEmpty(msg.Command))
            return Fail(id, ErrorCode.BadMessage, "command がありません");

        var a = msg.Args;
        try
        {
            switch (msg.Command)
            {
                // ---- モード / LTC 受信 ----
                case "mode.set":
                {
                    var mode = Str(a, "mode");
                    if (mode is not ("ltc" or "generate")) return Validation(id, "mode", "ltc または generate");
                    _engine.Stop();
                    _state.LtcStarted = false;
                    _state.GeneratorRunning = false;
                    _state.Mode = mode;
                    _state.SetError(null);
                    _state.MarkDirty(Domain.Mode | Domain.Transport | Domain.Generator | Domain.Receive);
                    return Ok(id);
                }
                case "ltc.start":
                case "ltc.reconnect":
                {
                    var deviceId = Str(a, "deviceId") ?? _state.SelectedInputDeviceId;
                    var device = _state.FindDevice(deviceId);
                    if (device is null) return Fail(id, ErrorCode.DeviceNotFound, "入力デバイスが見つかりません。デバイス一覧を更新して選び直してください");
                    try
                    {
                        _engine.Stop();
                        _engine.StartLtc(device.Id, device.IsLoopback);
                    }
                    catch (Exception ex)
                    {
                        _state.LtcStarted = false;
                        _state.SetError(ex.Message);
                        return Fail(id, ErrorCode.AudioError, $"音声入力を開けません: {ex.Message}", retryable: true);
                    }
                    _state.SelectedInputDeviceId = device.Id;
                    _state.LtcStarted = true;
                    _state.SetError(null);
                    _state.MarkDirty(Domain.Receive | Domain.Transport);
                    return Ok(id);
                }
                case "ltc.stop":
                    _engine.Stop();
                    _state.LtcStarted = false;
                    _state.MarkDirty(Domain.Transport);
                    return Ok(id);
                case "audio.refreshDevices":
                    _state.MarkDirty(Domain.Receive | Domain.Generator);
                    return Ok(id);

                // ---- 受信設定 ----
                case "receive.setOffset":
                {
                    var text = Str(a, "value") ?? "";
                    if (!TimecodeOffset.TryParse(text, _engine.FrameRate, out var offset))
                        return Validation(id, "value", "±HH:MM:SS:FF の形式で入力してください");
                    _engine.Offset = offset;
                    _state.MarkDirty(Domain.Receive | Domain.NextCue);
                    return Ok(id, new { normalized = offset.ToString() });
                }
                case "receive.setTriggerWindow":
                {
                    var frames = Int(a, "frames");
                    if (frames is null or < 0 or > 300) return Validation(id, "frames", "0〜300 の整数");
                    _cues.TriggerWindowFrames = frames.Value;
                    _state.MarkDirty(Domain.Receive);
                    return Ok(id, new { frames = frames.Value });
                }
                case "receive.setFreerunDuration":
                {
                    var seconds = Num(a, "seconds");
                    if (seconds is null or < 0 or > 3600) return Validation(id, "seconds", "0〜3600 秒");
                    _engine.FreerunDurationSeconds = seconds.Value;
                    _state.MarkDirty(Domain.Receive);
                    return Ok(id);
                }

                // ---- 内部生成 ----
                case "generator.configure":
                {
                    var g = _state.Generator;
                    if (Str(a, "frameRate") is { } rateCode)
                    {
                        if (!HostState.TryParseFrameRate(rateCode, out var rate)) return Validation(id, "frameRate", "24 / 25 / 29.97df / 30");
                        g.FrameRate = rate;
                    }
                    if (Str(a, "startTime") is { } start)
                    {
                        if (!HostState.TryParseTimecode(start, g.FrameRate, out var tc)) return Validation(id, "startTime", "HH:MM:SS:FF の形式で入力してください");
                        g.StartTime = tc;
                    }
                    else
                    {
                        g.StartTime = new TimecodeValue(g.StartTime.Hours, g.StartTime.Minutes, g.StartTime.Seconds, Math.Min(g.StartTime.Frames, g.FrameRate.FramesPerSecond() - 1), g.FrameRate);
                    }
                    if (Has(a, "outputDeviceId")) g.OutputDeviceId = Str(a, "outputDeviceId") ?? string.Empty;
                    if (Num(a, "volume") is { } vol)
                    {
                        if (vol < 0 || vol > 1) return Validation(id, "volume", "0〜1");
                        g.VolumeLevel = (float)vol;
                    }
                    _state.MarkDirty(Domain.Generator);
                    return Ok(id);
                }
                case "generator.start":
                    try
                    {
                        if (_engine.ActiveSource == TimecodeSourceType.Generator && !_state.GeneratorRunning && _engine.CurrentRawTimecode.ToOrdinal() > 0)
                            _engine.ResumeGenerator();
                        else
                            _engine.StartGenerator(_state.Generator);
                    }
                    catch (Exception ex)
                    {
                        _state.SetError(ex.Message);
                        return Fail(id, ErrorCode.AudioError, $"内部生成を開始できません: {ex.Message}", retryable: true);
                    }
                    _state.GeneratorRunning = true;
                    _state.SetError(null);
                    _state.MarkDirty(Domain.Generator | Domain.Transport);
                    return Ok(id);
                case "generator.stop":
                    _engine.StopGenerator();
                    _state.GeneratorRunning = false;
                    _state.MarkDirty(Domain.Generator | Domain.Transport);
                    return Ok(id);
                case "generator.reset":
                    _engine.ResetGenerator(_state.Generator.StartTime);
                    _state.MarkDirty(Domain.Generator | Domain.NextCue);
                    return Ok(id);

                // ---- ミュート ----
                case "mute.set":
                {
                    var muted = Bool(a, "muted");
                    if (muted is null) return Validation(id, "muted", "true/false");
                    _cues.IsMuted = muted.Value;
                    return Ok(id);
                }
                case "autoMute.setEnabled":
                {
                    var enabled = Bool(a, "enabled");
                    if (enabled is null) return Validation(id, "enabled", "true/false");
                    _cues.IsAutoMuteEnabled = enabled.Value;
                    _state.MarkDirty(Domain.Transport);
                    return Ok(id);
                }

                // ---- キュー(M2a: 一覧操作・発火) ----
                case "cue.setEnabled":
                {
                    var cueId = Str(a, "id"); var enabled = Bool(a, "enabled");
                    if (cueId is null || enabled is null) return Validation(id, "id/enabled", "必須");
                    if (FindCue(cueId) is null) return Fail(id, ErrorCode.NotFound, "キューが見つかりません");
                    _cues.SetCueEnabled(cueId, enabled.Value);
                    _state.MarkDirty(Domain.Cues);
                    return Ok(id);
                }
                case "cue.fire":
                {
                    var cueId = Str(a, "id");
                    if (cueId is null || FindCue(cueId) is null) return Fail(id, ErrorCode.NotFound, "キューが見つかりません");
                    _cues.ManualTrigger(cueId);
                    return Ok(id, new { sent = true });
                }
                case "cue.remove":
                {
                    var ids = StrArray(a, "ids");
                    if (ids.Length == 0) return Validation(id, "ids", "1 件以上");
                    foreach (var cueId in ids) if (FindCue(cueId) is not null) _cues.RemoveCue(cueId);
                    _state.MarkDirty(Domain.Cues);
                    return Ok(id);
                }
                case "cue.sortByTime":
                    _cues.ReorderCues(_cues.Cues.OrderBy(c => c.GetEffectiveTriggerTime().ToOrdinal()).Select(c => c.Id).ToArray());
                    _state.MarkDirty(Domain.Cues);
                    return Ok(id);
                case "cue.add":
                case "cue.update":
                case "cue.duplicate":
                case "cue.batchUpdate":
                case "project.new":
                case "project.open":
                case "project.save":
                case "project.saveAs":
                case "app.undo":
                case "app.redo":
                    return Fail(id, ErrorCode.InvalidState, $"{msg.Command} はまだ実装されていません(M2b)");

                // ---- CUE SYNC ----
                case "cueSync.configure":
                    if (Str(a, "oscAddress") is { } addr)
                    {
                        if (!addr.StartsWith('/')) return Validation(id, "oscAddress", "/ で始まる OSC アドレス");
                        _state.CueSync.OscAddress = addr;
                    }
                    if (Has(a, "targetHostIds")) { _state.CueSync.TargetHostIds.Clear(); _state.CueSync.TargetHostIds.AddRange(StrArray(a, "targetHostIds")); }
                    _state.MarkDirty(Domain.CueSync);
                    return Ok(id);
                case "cueSync.send":
                    _cues.SendCueSync(_state.CueSync.OscAddress, _state.CueSync.TargetHostIds);
                    return Ok(id, new { sent = true });

                // ---- 送信先ホスト ----
                case "host.add":
                case "host.update":
                {
                    var h = Get(a, "host");
                    var name = Str(h, "name")?.Trim(); var ip = Str(h, "ipAddress")?.Trim(); var port = Int(h, "port");
                    if (string.IsNullOrEmpty(name)) return Validation(id, "host.name", "必須");
                    if (!OscHost.TryParseIpAddress(ip, out _)) return Validation(id, "host.ipAddress", "IPv4/IPv6 アドレス");
                    if (port is null or < 1 or > 65535) return Validation(id, "host.port", "1〜65535");
                    var enabled = Bool(h, "enabled") ?? true;
                    if (msg.Command == "host.add")
                    {
                        var host = new OscHost { Id = Guid.NewGuid().ToString("N"), Name = name, IpAddress = ip!, Port = port.Value, IsEnabled = enabled };
                        _hosts.AddHost(host);
                        return Ok(id, new { id = host.Id });
                    }
                    var hostId = Str(a, "id");
                    if (hostId is null || _hosts.Hosts.All(x => x.Id != hostId)) return Fail(id, ErrorCode.NotFound, "ホストが見つかりません");
                    _hosts.UpdateHost(hostId, new OscHost { Id = hostId, Name = name, IpAddress = ip!, Port = port.Value, IsEnabled = enabled });
                    return Ok(id, new { id = hostId });
                }
                case "host.remove":
                {
                    var hostId = Str(a, "id");
                    if (hostId is null || _hosts.Hosts.All(x => x.Id != hostId)) return Fail(id, ErrorCode.NotFound, "ホストが見つかりません");
                    _hosts.RemoveHost(hostId);
                    return Ok(id);
                }
                case "host.setEnabled":
                {
                    var hostId = Str(a, "id"); var enabled = Bool(a, "enabled");
                    if (hostId is null || enabled is null) return Validation(id, "id/enabled", "必須");
                    _hosts.SetHostEnabled(hostId, enabled.Value);
                    return Ok(id);
                }
                case "host.ping":
                {
                    var hostId = Str(a, "id");
                    if (hostId is null || _hosts.Hosts.All(x => x.Id != hostId)) return Fail(id, ErrorCode.NotFound, "ホストが見つかりません");
                    _osc.SendPing(hostId);
                    return Ok(id);
                }

                // ---- タイムコード中継 ----
                case "relay.configure":
                    if (Str(a, "oscAddressPattern") is { } pattern) _relay.OscAddressPattern = pattern;
                    if (Get(a, "interval") is { } iv)
                    {
                        var mode = Str(iv, "mode") == "custom" ? RelayIntervalMode.Custom : RelayIntervalMode.EveryFrame;
                        var ms = Int(iv, "intervalMs") ?? 0;
                        if (mode == RelayIntervalMode.Custom && ms < 1) return Validation(id, "interval.intervalMs", "1 以上");
                        _relay.ContinuousInterval = new(mode, ms);
                    }
                    if (Has(a, "targetHostIds")) _relay.TargetHostIds = StrArray(a, "targetHostIds");
                    _state.MarkDirty(Domain.Relay);
                    return Ok(id);
                case "relay.setContinuous":
                {
                    var enabled = Bool(a, "enabled");
                    if (enabled is null) return Validation(id, "enabled", "true/false");
                    _relay.IsContinuousEnabled = enabled.Value;
                    _state.MarkDirty(Domain.Relay);
                    return Ok(id);
                }
                case "relay.sendOnce":
                    _relay.TriggerOneShot();
                    return Ok(id, new { sent = true });

                // ---- OSC ポン出し ----
                case "triggerPanel.configureGrid":
                {
                    var rows = Int(a, "rows"); var cols = Int(a, "columns");
                    if (rows is null or < 1 or > 12 || cols is null or < 1 or > 12) return Validation(id, "rows/columns", "1〜12");
                    _panel.SetGridSize(rows.Value, cols.Value);
                    return Ok(id);
                }
                case "triggerPanel.upsertButton":
                {
                    var b = Get(a, "button");
                    var row = Int(b, "row"); var col = Int(b, "column");
                    if (row is null || col is null) return Validation(id, "button.row/column", "必須");
                    var buttonId = Str(b, "id") ?? Guid.NewGuid().ToString("N");
                    var existing = _panel.GetButtonAt(row.Value, col.Value);
                    if (existing is not null && existing.Id != buttonId) return Fail(id, ErrorCode.Conflict, "そのセルには別のボタンがあります");
                    _panel.UpsertButton(new OscTriggerButton
                    {
                        Id = buttonId, Row = row.Value, Column = col.Value,
                        Label = Str(b, "label") ?? "", OscAddress = Str(b, "oscAddress") ?? "",
                        Arguments = OscArgs(Get(b, "arguments")), TargetHostIds = StrArray(b, "targetHostIds").ToList(),
                    });
                    return Ok(id, new { id = buttonId });
                }
                case "triggerPanel.removeButton":
                {
                    var buttonId = Str(a, "id");
                    if (buttonId is null) return Validation(id, "id", "必須");
                    _panel.RemoveButton(buttonId);
                    return Ok(id);
                }
                case "triggerPanel.fire":
                {
                    var buttonId = Str(a, "id");
                    if (buttonId is null) return Validation(id, "id", "必須");
                    var r = _panel.Trigger(buttonId);
                    return Ok(id, new { sent = r.Sent, reason = r.Reason.ToString() });
                }

                // ---- その他 ----
                case "logs.clear":
                    _state.ClearLogs();
                    return Ok(id);
                case "app.requestClose":
                    CloseRequested?.Invoke();
                    return Ok(id, new { cancelled = false });

                default:
                    return Fail(id, ErrorCode.UnknownCommand, $"未知の command: {msg.Command}");
            }
        }
        catch (Exception ex)
        {
            var errorId = Guid.NewGuid().ToString("N")[..8];
            _state.AppendLog($"[{errorId}] {msg.Command} で内部エラー: {ex}", false);
            return Fail(id, ErrorCode.Internal, $"内部エラー({errorId})");
        }
    }

    private Cue? FindCue(string cueId) => _cues.Cues.FirstOrDefault(c => c.Id == cueId);

    // ---- 結果 ----

    private static ResultMessage Ok(string id, object? data = null) => ResultMessage.Success(id, data);

    private static ResultMessage Fail(string id, string code, string message, bool retryable = false) =>
        ResultMessage.Failure(id, new ProtocolError(code, message, retryable));

    private static ResultMessage Validation(string id, string field, string expectation) =>
        ResultMessage.Failure(id, new ProtocolError(ErrorCode.Validation, $"{field}: {expectation}", false, new Dictionary<string, string> { [field] = expectation }));

    // ---- args 読み出し ----

    private static JsonElement? Get(JsonElement? obj, string name) =>
        obj is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v : null;

    private static bool Has(JsonElement? obj, string name) =>
        obj is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out _);

    private static string? Str(JsonElement? obj, string name) =>
        Get(obj, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    private static int? Int(JsonElement? obj, string name) =>
        Get(obj, name) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out var i) ? i : null;

    private static double? Num(JsonElement? obj, string name) =>
        Get(obj, name) is { ValueKind: JsonValueKind.Number } v ? v.GetDouble() : null;

    private static bool? Bool(JsonElement? obj, string name) =>
        Get(obj, name) is { } v && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    private static string[] StrArray(JsonElement? obj, string name) =>
        Get(obj, name) is { ValueKind: JsonValueKind.Array } arr
            ? arr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray()
            : Array.Empty<string>();

    private static List<OscArgument> OscArgs(JsonElement? arr)
    {
        var list = new List<OscArgument>();
        if (arr is not { ValueKind: JsonValueKind.Array } items) return list;
        foreach (var e in items.EnumerateArray())
        {
            var type = Str(e, "type");
            var v = Get(e, "value");
            switch (type)
            {
                case "int32" when v is { ValueKind: JsonValueKind.Number } n && n.TryGetInt32(out var i): list.Add(new OscInt32Argument(i)); break;
                case "float32" when v is { ValueKind: JsonValueKind.Number } n: list.Add(new OscFloat32Argument((float)n.GetDouble())); break;
                case "string": list.Add(new OscStringArgument(v is { ValueKind: JsonValueKind.String } s ? s.GetString()! : "")); break;
            }
        }
        return list;
    }
}
