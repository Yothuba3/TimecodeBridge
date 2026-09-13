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
    private readonly IOscTriggerPanelManager _panel;
    private readonly ProjectCoordinator _projects;

    public CommandRouter(HostState state, ITimecodeEngine engine, ICueManager cues, IHostRegistry hosts,
        IOscTriggerPanelManager panel, ProjectCoordinator projects)
    {
        _state = state; _engine = engine; _cues = cues; _hosts = hosts; _panel = panel; _projects = projects;
    }

    public Action? CloseRequested { get; set; }

    /// <summary>メニューなど Web 以外の起点から同じ経路で command を流すためのメッセージ。</summary>
    public static WebMessage Synthetic(string requestId, string command, object? args = null) =>
        new(Protocol.Version, "command", requestId, command, args is null ? null : JsonSerializer.SerializeToElement(args, Protocol.Json), null, null, null, null, null, null, null, null);

    public async Task<ResultMessage> ExecuteAsync(WebMessage msg)
    {
        var id = msg.RequestId ?? "";
        if (msg.ProtocolVersion != Protocol.Version)
            return Fail(id, ErrorCode.UnsupportedVersion, $"protocolVersion {msg.ProtocolVersion} は未対応です");
        if (string.IsNullOrEmpty(msg.Command))
            return Fail(id, ErrorCode.BadMessage, "command がありません");

        // ダイアログを伴うものだけ非同期。それ以外は同期処理へ
        try
        {
            switch (msg.Command)
            {
                case "project.new":
                    return Ok(id, new { cancelled = !await _projects.NewAsync() });
                case "project.open":
                {
                    var path = Str(msg.Args, "path");
                    if (path is not null && !File.Exists(path)) return Fail(id, ErrorCode.NotFound, $"ファイルがありません: {path}");
                    try
                    {
                        var (cancelled, opened) = await _projects.OpenAsync(path);
                        return Ok(id, new { cancelled, path = opened });
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
                    {
                        _state.AppendLog($"プロジェクトを開けません: {ex.Message}", false);
                        return Fail(id, ErrorCode.IoError, $"プロジェクトを開けません: {ex.Message}");
                    }
                }
                case "host.ping":
                {
                    var hostId = Str(msg.Args, "id");
                    var host = hostId is null ? null : _hosts.Hosts.FirstOrDefault(x => x.Id == hostId);
                    if (host is null) return Fail(id, ErrorCode.NotFound, "ホストが見つかりません");
                    _state.SetHostReachability(host.Id, "checking");
                    var (reachable, latencyMs) = await PingAsync(host.IpAddress);
                    _state.SetHostReachability(host.Id, reachable ? "reachable" : "unreachable");
                    _state.AppendLog(reachable ? $"ping {host.Name} ({host.IpAddress}) 応答 {latencyMs}ms" : $"ping {host.Name} ({host.IpAddress}) 応答なし", reachable);
                    return Ok(id, new { reachable, latencyMs });
                }
                case "project.save":
                case "project.saveAs":
                {
                    try
                    {
                        var (cancelled, saved) = await _projects.SaveAsync(msg.Command == "project.saveAs", Str(msg.Args, "suggestedName"));
                        return Ok(id, new { cancelled, path = saved });
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        _state.AppendLog($"保存に失敗: {ex.Message}", false);
                        return Fail(id, ErrorCode.IoError, $"保存に失敗しました: {ex.Message}", retryable: true);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            var errorId = Guid.NewGuid().ToString("N")[..8];
            _state.AppendLog($"[{errorId}] {msg.Command} で内部エラー: {ex}", false);
            return Fail(id, ErrorCode.Internal, $"内部エラー({errorId})");
        }
        return Execute(msg);
    }

    public ResultMessage Execute(WebMessage msg)
    {
        var id = msg.RequestId ?? "";
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
                    // LTC 受信へ戻ったときは、選択済みの入力デバイスがあればそのまま受信を再開する(再接続の手間を省く)
                    if (mode == "ltc" && _state.FindDevice(_state.SelectedInputDeviceId) is { } input)
                    {
                        try
                        {
                            _engine.StartLtc(input.Id, input.IsLoopback);
                            _state.LtcStarted = true;
                        }
                        catch (Exception ex)
                        {
                            _state.SetError($"音声入力を開けません: {ex.Message}");
                        }
                    }
                    _state.MarkDirty(Domain.Mode | Domain.Transport | Domain.Generator | Domain.Receive);
                    return Ok(id, new { ltcStarted = _state.LtcStarted });
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
                case "receive.selectDevice":
                {
                    // deviceId が null/空なら「未選択」に戻す(受信を止めて選択を消す)。それ以外は ltc.reconnect と同じ
                    var deviceId = Str(a, "deviceId");
                    if (!string.IsNullOrEmpty(deviceId))
                        return Execute(msg with { Command = "ltc.reconnect" });
                    _engine.Stop();
                    _state.LtcStarted = false;
                    _state.SelectedInputDeviceId = null;
                    _state.SetError(null);
                    _state.MarkDirty(Domain.Receive | Domain.Transport);
                    return Ok(id);
                }
                case "audio.refreshDevices":
                    _state.MarkDirty(Domain.Receive | Domain.Generator);
                    return Ok(id);

                // ---- 受信設定 ----
                case "receive.setOffset":
                {
                    var text = Str(a, "value") ?? "";
                    if (!TimecodeOffset.TryParse(HostState.NormalizeTimecodeText(text), _engine.FrameRate, out var offset))
                        return Validation(id, "value", "±HH:MM:SS:FF の形式で入力してください");
                    _engine.Offset = offset;
                    _projects.Commit();
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
                    _projects.Commit();
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
                    _projects.Commit();
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
                    _projects.Commit();
                    _state.MarkDirty(Domain.Cues);
                    return Ok(id);
                }
                case "cue.sortByTime":
                    _cues.ReorderCues(_cues.Cues.OrderBy(c => c.GetEffectiveTriggerTime().ToOrdinal()).Select(c => c.Id).ToArray());
                    _projects.Commit();
                    _state.MarkDirty(Domain.Cues);
                    return Ok(id);

                // ---- キュー(M2b: 追加・編集・複製・一括編集) ----
                case "cue.add":
                case "cue.update":
                {
                    string? cueId = null;
                    if (msg.Command == "cue.update")
                    {
                        cueId = Str(a, "id");
                        if (cueId is null || FindCue(cueId) is null) return Fail(id, ErrorCode.NotFound, "キューが見つかりません");
                    }
                    var draft = Get(a, "cue");
                    if (draft is null) return Validation(id, "cue", "必須");
                    var parsed = ParseCueDraft(draft, cueId ?? Guid.NewGuid().ToString(), _engine.FrameRate);
                    if (parsed.Error is { } err) return Validation(id, err.Field, err.Message);
                    if (cueId is null) _cues.AddCue(parsed.Cue!); else _cues.UpdateCue(cueId, parsed.Cue!);
                    _projects.Commit();
                    _state.MarkDirty(Domain.Cues);
                    return Ok(id, new { id = parsed.Cue!.Id });
                }
                case "cue.duplicate":
                {
                    var cueId = Str(a, "id");
                    var source = cueId is null ? null : FindCue(cueId);
                    if (source is null) return Fail(id, ErrorCode.NotFound, "キューが見つかりません");
                    int count = Int(a, "count") ?? 1;
                    long interval = Int(a, "intervalFrames") ?? 0;
                    if (Str(a, "interval") is { } intervalText && intervalText.Trim().Length > 0)
                    {
                        if (!HostState.TryParseTimecode(intervalText, source.TriggerTime.FrameRate, out var intervalValue)) return Validation(id, "interval", "HH:MM:SS:FF の形式で入力してください");
                        interval = intervalValue.TotalFrames();
                    }
                    if (count is < 1 or > 500) return Validation(id, "count", "1〜500");
                    if (interval < 0) return Validation(id, "intervalFrames", "0 以上");
                    var ids = new List<string>();
                    if (count == 1 && interval == 0)
                    {
                        var copy = CloneCue(source, source.TriggerTime, source.Name + " (コピー)");
                        _cues.AddCue(copy); ids.Add(copy.Id);
                    }
                    else
                    {
                        long baseFrames = source.TriggerTime.TotalFrames();
                        var rate = source.TriggerTime.FrameRate;
                        long maximumFrames = new TimecodeValue(23, 59, 59, rate.FramesPerSecond() - 1, rate).TotalFrames();
                        if (baseFrames + interval * count > maximumFrames)
                            return Validation(id, "interval", "複製後のトリガー時間が 23:59:59:FF を超えます");
                        for (int i = 1; i <= count; i++)
                        {
                            var copy = CloneCue(source, TimecodeValue.FromTotalFrames(baseFrames + interval * i, source.TriggerTime.FrameRate));
                            _cues.AddCue(copy); ids.Add(copy.Id);
                        }
                    }
                    _projects.Commit();
                    _state.MarkDirty(Domain.Cues);
                    return Ok(id, new { ids });
                }
                case "cue.batchUpdate":
                {
                    var ids = StrArray(a, "ids");
                    var changes = Get(a, "changes");
                    if (ids.Length == 0) return Validation(id, "ids", "1 件以上");
                    if (changes is null) return Validation(id, "changes", "必須");
                    var batch = ParseBatchDraft(changes, _engine.FrameRate);
                    if (batch.Error is { } berr) return Validation(id, berr.Field, berr.Message);
                    int updated = 0, offsetSkipped = 0;
                    foreach (var cueId in ids)
                    {
                        var cue = FindCue(cueId);
                        if (cue is null) continue;
                        var next = CloneCue(cue, cue.TriggerTime, cue.Name, keepId: true);
                        batch.Apply(next, ref offsetSkipped);
                        _cues.UpdateCue(cueId, next);
                        updated++;
                    }
                    if (updated > 0) _projects.Commit();
                    _state.MarkDirty(Domain.Cues);
                    return Ok(id, new { updated, offsetSkipped });
                }
                case "app.undo":
                    if (!_projects.Undo()) return Fail(id, ErrorCode.InvalidState, "取り消せる変更がありません");
                    return Ok(id);
                case "app.redo":
                    if (!_projects.Redo()) return Fail(id, ErrorCode.InvalidState, "やり直せる変更がありません");
                    return Ok(id);

                // ---- CUE SYNC ----
                case "cueSync.configure":
                    if (Str(a, "oscAddress") is { } addr)
                    {
                        if (!addr.StartsWith('/')) return Validation(id, "oscAddress", "/ で始まる OSC アドレス");
                        _state.CueSync.OscAddress = addr;
                    }
                    if (Has(a, "targetHostIds")) { _state.CueSync.TargetHostIds.Clear(); _state.CueSync.TargetHostIds.AddRange(StrArray(a, "targetHostIds")); }
                    _projects.Commit();
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
                        _projects.Commit();
                        return Ok(id, new { id = host.Id });
                    }
                    var hostId = Str(a, "id");
                    if (hostId is null || _hosts.Hosts.All(x => x.Id != hostId)) return Fail(id, ErrorCode.NotFound, "ホストが見つかりません");
                    _hosts.UpdateHost(hostId, new OscHost { Id = hostId, Name = name, IpAddress = ip!, Port = port.Value, IsEnabled = enabled });
                    _projects.Commit();
                    return Ok(id, new { id = hostId });
                }
                case "host.remove":
                {
                    var hostId = Str(a, "id");
                    if (hostId is null || _hosts.Hosts.All(x => x.Id != hostId)) return Fail(id, ErrorCode.NotFound, "ホストが見つかりません");
                    _hosts.RemoveHost(hostId);
                    _projects.Commit();
                    return Ok(id);
                }
                case "host.setEnabled":
                {
                    var hostId = Str(a, "id"); var enabled = Bool(a, "enabled");
                    if (hostId is null || enabled is null) return Validation(id, "id/enabled", "必須");
                    _hosts.SetHostEnabled(hostId, enabled.Value);
                    _projects.Commit();
                    return Ok(id);
                }

                // ---- OSC ポン出し ----
                case "triggerPanel.configureGrid":
                {
                    var rows = Int(a, "rows"); var cols = Int(a, "columns");
                    if (rows is null or < 1 or > 12 || cols is null or < 1 or > 12) return Validation(id, "rows/columns", "1〜12");
                    _panel.SetGridSize(rows.Value, cols.Value);
                    _projects.Commit();
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
                    _projects.Commit();
                    return Ok(id, new { id = buttonId });
                }
                case "triggerPanel.removeButton":
                {
                    var buttonId = Str(a, "id");
                    if (buttonId is null) return Validation(id, "id", "必須");
                    _panel.RemoveButton(buttonId);
                    _projects.Commit();
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

    /// <summary>ICMP で疎通と往復時間を測る(3 秒で打ち切り)。</summary>
    private static async Task<(bool Reachable, long? LatencyMs)> PingAsync(string ipAddress)
    {
        try
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            var reply = await ping.SendPingAsync(ipAddress, 3000);
            return reply.Status == System.Net.NetworkInformation.IPStatus.Success ? (true, reply.RoundtripTime) : (false, null);
        }
        catch (Exception)
        {
            return (false, null);
        }
    }

    // ---- キュー下書きの解釈(protocol の CueDraft / CueBatchDraft) ----

    private sealed record DraftError(string Field, string Message);

    private static (Cue? Cue, DraftError? Error) ParseCueDraft(JsonElement? d, string cueId, FrameRate defaultRate)
    {
        var name = Str(d, "name")?.Trim();
        if (string.IsNullOrEmpty(name)) return (null, new("cue.name", "必須"));

        var rate = defaultRate;
        if (Str(d, "frameRate") is { } rateCode && !HostState.TryParseFrameRate(rateCode, out rate)) return (null, new("cue.frameRate", "24 / 25 / 29.97df / 30"));
        if (!HostState.TryParseTimecode(Str(d, "triggerTime"), rate, out var trigger)) return (null, new("cue.triggerTime", "HH:MM:SS:FF の形式で入力してください"));

        var osc = Str(d, "oscAddress")?.Trim() ?? "";
        if (!osc.StartsWith('/')) return (null, new("cue.oscAddress", "OSCアドレスは '/' で始まる必要があります"));
        var additional = StrArray(d, "additionalOscAddresses").Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (additional.Any(x => !x.StartsWith('/'))) return (null, new("cue.additionalOscAddresses", "追加アドレスも '/' で始まる必要があります"));

        TimecodeOffset? triggerOffset = null;
        if (Str(d, "triggerOffset") is { } offsetText && offsetText.Trim().Length > 0)
        {
            if (!TimecodeOffset.TryParse(HostState.NormalizeTimecodeText(offsetText), rate, out var parsedOffset)) return (null, new("cue.triggerOffset", "±HH:MM:SS:FF の形式で入力してください"));
            if (parsedOffset.TotalFrames() != 0) triggerOffset = parsedOffset;
        }
        if (!Cue.TryApplyTriggerOffset(trigger, triggerOffset, out _))
            return (null, new("cue.triggerOffset", "トリガーオフセット適用後の発火時刻が 00:00:00:00〜23:59:59:FF の範囲を超えます"));

        TimecodeValue? sendTimecode = null;
        if (Str(d, "sendTimecode") is { } sendText && sendText.Trim().Length > 0)
        {
            if (!HostState.TryParseTimecode(sendText, rate, out var st)) return (null, new("cue.sendTimecode", "HH:MM:SS:FF の形式で入力してください"));
            sendTimecode = st;
        }
        TimecodeValue? autoUnmuteAfter = null;
        if (Str(d, "autoUnmuteAfter") is { } unmuteText && unmuteText.Trim().Length > 0)
        {
            if (!HostState.TryParseTimecode(unmuteText, rate, out var au)) return (null, new("cue.autoUnmuteAfter", "HH:MM:SS:FF の形式で入力してください"));
            autoUnmuteAfter = au;
        }

        return (new Cue
        {
            Id = cueId,
            Name = name,
            Memo = Str(d, "memo") ?? "",
            TriggerTime = trigger,
            OscAddress = osc,
            AdditionalOscAddresses = additional,
            Arguments = OscArgs(Get(d, "arguments")),
            TargetHostIds = StrArray(d, "targetHostIds").ToList(),
            IsEnabled = Bool(d, "enabled") ?? true,
            SendTriggerTimeAsSeconds = Bool(d, "sendTriggerTimeAsSeconds") ?? false,
            SendTimecode = sendTimecode,
            TriggerOffset = triggerOffset,
            AutoMuteOnFire = Bool(d, "autoMuteOnFire") ?? false,
            AutoUnmuteAfter = autoUnmuteAfter,
        }, null);
    }

    /// <summary>一括編集。存在するフィールドだけを適用する(null は「クリア」)。</summary>
    private sealed class BatchDraft
    {
        public DraftError? Error;
        public string? OscAddress; public List<string>? AdditionalOscAddresses; public List<OscArgument>? Arguments; public List<string>? TargetHostIds;
        public string? Memo; public bool? Enabled; public bool? SendTriggerTimeAsSeconds; public bool? AutoMuteOnFire;
        public bool HasSendTimecode; public TimecodeValue? SendTimecode;
        public bool HasTriggerOffset; public TimecodeOffset? TriggerOffset;
        public bool HasAutoUnmuteAfter; public TimecodeValue? AutoUnmuteAfter;

        public void Apply(Cue cue, ref int offsetSkipped)
        {
            if (OscAddress is not null) cue.OscAddress = OscAddress;
            if (AdditionalOscAddresses is not null) cue.AdditionalOscAddresses = AdditionalOscAddresses.ToList();
            if (Arguments is not null) cue.Arguments = Arguments.ToList();
            if (TargetHostIds is not null) cue.TargetHostIds = TargetHostIds.ToList();
            if (Memo is not null) cue.Memo = Memo;
            if (Enabled is { } e) cue.IsEnabled = e;
            if (SendTriggerTimeAsSeconds is { } s) cue.SendTriggerTimeAsSeconds = s;
            if (AutoMuteOnFire is { } m) cue.AutoMuteOnFire = m;
            if (HasSendTimecode) cue.SendTimecode = SendTimecode;
            if (HasAutoUnmuteAfter) cue.AutoUnmuteAfter = AutoUnmuteAfter;
            if (HasTriggerOffset)
            {
                // 適用後の発火時刻が 0〜24 時を超えるキューにはオフセットを付けず、件数だけ返す
                if (Cue.TryApplyTriggerOffset(cue.TriggerTime, TriggerOffset, out _)) cue.TriggerOffset = TriggerOffset;
                else offsetSkipped++;
            }
        }
    }

    private static BatchDraft ParseBatchDraft(JsonElement? c, FrameRate rate)
    {
        var b = new BatchDraft();
        if (Has(c, "oscAddress"))
        {
            var osc = Str(c, "oscAddress")?.Trim() ?? "";
            if (!osc.StartsWith('/')) { b.Error = new("changes.oscAddress", "OSCアドレスは '/' で始まる必要があります"); return b; }
            b.OscAddress = osc;
        }
        if (Has(c, "additionalOscAddresses"))
        {
            var list = StrArray(c, "additionalOscAddresses").Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (list.Any(x => !x.StartsWith('/'))) { b.Error = new("changes.additionalOscAddresses", "追加アドレスも '/' で始まる必要があります"); return b; }
            b.AdditionalOscAddresses = list;
        }
        if (Has(c, "arguments")) b.Arguments = OscArgs(Get(c, "arguments"));
        if (Has(c, "targetHostIds")) b.TargetHostIds = StrArray(c, "targetHostIds").ToList();
        if (Has(c, "memo")) b.Memo = Str(c, "memo") ?? "";
        if (Has(c, "enabled")) b.Enabled = Bool(c, "enabled");
        if (Has(c, "sendTriggerTimeAsSeconds")) b.SendTriggerTimeAsSeconds = Bool(c, "sendTriggerTimeAsSeconds");
        if (Has(c, "autoMuteOnFire")) b.AutoMuteOnFire = Bool(c, "autoMuteOnFire");
        if (Has(c, "sendTimecode"))
        {
            b.HasSendTimecode = true;
            if (Str(c, "sendTimecode") is { } t && t.Trim().Length > 0)
            {
                if (!HostState.TryParseTimecode(t, rate, out var st)) { b.Error = new("changes.sendTimecode", "HH:MM:SS:FF の形式で入力してください"); return b; }
                b.SendTimecode = st;
            }
        }
        if (Has(c, "autoUnmuteAfter"))
        {
            b.HasAutoUnmuteAfter = true;
            if (Str(c, "autoUnmuteAfter") is { } t && t.Trim().Length > 0)
            {
                if (!HostState.TryParseTimecode(t, rate, out var au)) { b.Error = new("changes.autoUnmuteAfter", "HH:MM:SS:FF の形式で入力してください"); return b; }
                b.AutoUnmuteAfter = au;
            }
        }
        if (Has(c, "triggerOffset"))
        {
            b.HasTriggerOffset = true;
            if (Str(c, "triggerOffset") is { } t && t.Trim().Length > 0)
            {
                if (!TimecodeOffset.TryParse(HostState.NormalizeTimecodeText(t), rate, out var off)) { b.Error = new("changes.triggerOffset", "±HH:MM:SS:FF の形式で入力してください"); return b; }
                if (off.TotalFrames() != 0) b.TriggerOffset = off;
            }
        }
        return b;
    }

    private static Cue CloneCue(Cue source, TimecodeValue triggerTime, string? name = null, bool keepId = false) => new()
    {
        Id = keepId ? source.Id : Guid.NewGuid().ToString(),
        Name = name ?? source.Name,
        Memo = source.Memo,
        TriggerTime = triggerTime,
        OscAddress = source.OscAddress,
        AdditionalOscAddresses = source.AdditionalOscAddresses.ToList(),
        Arguments = source.Arguments.ToList(),
        TargetHostIds = source.TargetHostIds.ToList(),
        IsEnabled = source.IsEnabled,
        SendTriggerTimeAsSeconds = source.SendTriggerTimeAsSeconds,
        SendTimecode = source.SendTimecode,
        TriggerOffset = source.TriggerOffset,
        AutoMuteOnFire = source.AutoMuteOnFire,
        AutoUnmuteAfter = source.AutoUnmuteAfter,
    };

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
