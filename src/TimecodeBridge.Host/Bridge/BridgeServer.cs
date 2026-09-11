using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;

namespace TimecodeBridge.Host.Bridge;

/// <summary>
/// NativeWebView との通信路。C#→JS は <c>window.tcb.receive(json)</c> の一入口へ InvokeScript、
/// JS→C# は <c>invokeCSharpAction(json)</c> を WebMessageReceived で受ける。
/// snapshot/patch/result は順序を保って送り、clock/wave は最新 1 件だけを送る(前回送信中なら中間値は捨てる)。
/// </summary>
public sealed class BridgeServer : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(33);
    // 環境変数 TIMECODEBRIDGE_BRIDGE_TRACE=1 で送受信の種別を標準出力へ出す(実機確認用)
    private static readonly bool Trace = Environment.GetEnvironmentVariable("TIMECODEBRIDGE_BRIDGE_TRACE") is "1" or "2";
    private static readonly bool TraceEveryClock = Environment.GetEnvironmentVariable("TIMECODEBRIDGE_BRIDGE_TRACE") == "2";

    private readonly NativeWebView _webView;
    private readonly HostState _state;
    private readonly CommandRouter _router;
    private readonly Queue<HostMessage> _reliable = new();
    private readonly DispatcherTimer _timer;
    private ClockMessage? _latestClock;
    private WaveMessage? _latestWave;
    private bool _sending;
    private bool _ready;
    private long _clockSeq;
    private long _waveSeq;
    private string? _lastClockKey;
    private int _wavePoints = 240;
    private bool _waveVisible = true;

    public BridgeServer(NativeWebView webView, HostState state, CommandRouter router, Action closeWindow)
    {
        _webView = webView;
        _state = state;
        _router = router;
        _router.CloseRequested = closeWindow;

        _webView.WebMessageReceived += OnWebMessage;
        _webView.NavigationCompleted += (_, _) => { _ready = false; _ = AnnounceAsync(); };
        _state.Changed += OnStateChanged;

        _timer = new DispatcherTimer(TickInterval, DispatcherPriority.Normal, (_, _) => Tick());
        _timer.Start();
    }

    /// <summary>
    /// ページ読込後、Web に「Host が接続済み」を知らせて ready を送らせる。
    /// invokeCSharpAction はページのスクリプト評価より後に注入されるため、Web 側が起動時に送った ready は届かない。
    /// ready が返るまで少し間隔を置いて繰り返す。
    /// </summary>
    private async Task AnnounceAsync()
    {
        for (int i = 0; i < 40 && !_ready; i++)
        {
            try { await _webView.InvokeScript("window.tcb&&window.tcb.hostAttached&&window.tcb.hostAttached()"); }
            catch (Exception ex) { if (Trace) Console.WriteLine($"[bridge] announce failed: {ex.Message}"); }
            await Task.Delay(250);
        }
    }

    // ---- 受信 ----

    private async void OnWebMessage(object? sender, WebMessageReceivedEventArgs e)
    {
        WebMessage? msg;
        if (Trace) Console.WriteLine($"[bridge] raw {(e.Body ?? "").Length}B: {(e.Body ?? "")[..Math.Min(160, (e.Body ?? "").Length)]}");
        try
        {
            msg = JsonSerializer.Deserialize<WebMessage>(e.Body ?? "", Protocol.Json);
        }
        catch (JsonException ex)
        {
            if (Trace) Console.WriteLine($"[bridge] parse error: {ex.Message}");
            _state.AppendLog($"Web からの不正なメッセージ: {ex.Message}", false);
            return;
        }
        if (msg is null) return;
        if (Trace) Console.WriteLine($"[bridge] <- {msg.Type} {msg.Command} {msg.RequestId}");

        switch (msg.Type)
        {
            case "ready":
                _ready = true;
                _lastClockKey = null;
                Enqueue(new SnapshotMessage(_state.Revision, _state.BuildSnapshot()));
                AutoStartInputIfRequested();
                break;
            case "resync":
                Enqueue(new SnapshotMessage(_state.Revision, _state.BuildSnapshot()));
                break;
            case "viewport":
                if (msg.WaveformWidth is { } w) _wavePoints = Math.Clamp(w / 2, 16, 2048);
                if (msg.Visible is { } v) _waveVisible = v;
                break;
            case "command":
                Enqueue(await _router.ExecuteAsync(msg));
                break;
            default:
                if (msg.RequestId is { } id)
                    Enqueue(ResultMessage.Failure(id, new ProtocolError(ErrorCode.BadMessage, $"未知の type: {msg.Type}")));
                break;
        }
    }

    private void OnStateChanged(StateChanges changes, long revision, long baseRevision)
    {
        if (Trace && changes.Transport is { } t) Console.WriteLine($"[bridge] transport {t.Status} '{t.StatusText}' {t.DetailText} err={t.SignalErrorRatePercent:F1}% locked={t.Locked}");
        if (!_ready) return;
        Enqueue(new PatchMessage(revision, baseRevision, changes));
    }

    // 環境変数 TIMECODEBRIDGE_AUTOSTART_INPUT=<入力デバイス名の一部> で、接続直後にそのデバイスで LTC 受信を始める(実機確認用)
    private bool _autoStarted;
    private void AutoStartInputIfRequested()
    {
        var name = Environment.GetEnvironmentVariable("TIMECODEBRIDGE_AUTOSTART_INPUT");
        if (_autoStarted || string.IsNullOrWhiteSpace(name)) return;
        _autoStarted = true;
        var device = _state.AllDevices().FirstOrDefault(d => d.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (device is null) { Console.WriteLine($"[bridge] autostart: device '{name}' not found"); return; }
        var args = JsonSerializer.SerializeToElement(new { deviceId = device.Id });
        var result = _router.ExecuteAsync(new WebMessage(Protocol.Version, "command", "autostart", "ltc.reconnect", args, null, null, null, null, null, null, null, null)).GetAwaiter().GetResult();
        Console.WriteLine($"[bridge] autostart {device.DisplayName}: ok={result.Ok} {result.Error?.Message}");
        Enqueue(result);
    }

    // ---- 送信 ----

    private void Tick()
    {
        if (!_ready) return;

        var clock = _state.BuildClock();
        var key = clock.Raw + clock.Display + clock.NextCueId;
        if (key != _lastClockKey)
        {
            _lastClockKey = key;
            _latestClock = new ClockMessage(++_clockSeq, clock);
            if (Trace && (_clockSeq % 30 == 1 || TraceEveryClock)) Console.WriteLine($"[bridge] clock#{_clockSeq} raw={clock.Raw} display={clock.Display} next={clock.NextCueId}");
        }
        if (_waveVisible && _state.TryBuildWave(_wavePoints) is { } wave)
            _latestWave = new WaveMessage(++_waveSeq, wave);

        _ = PumpAsync();
    }

    private void Enqueue(HostMessage message)
    {
        _reliable.Enqueue(message);
        _ = PumpAsync();
    }

    private async Task PumpAsync()
    {
        if (_sending) return;
        _sending = true;
        try
        {
            while (_reliable.Count > 0 || _latestClock is not null || _latestWave is not null)
            {
                HostMessage next;
                if (_reliable.Count > 0) next = _reliable.Dequeue();
                else if (_latestClock is not null) { next = _latestClock; _latestClock = null; }
                else { next = _latestWave!; _latestWave = null; }

                if (!_ready && next is not SnapshotMessage) continue;

                var json = JsonSerializer.Serialize(next, next.GetType(), Protocol.Json);
                if (Trace && next is not ClockMessage && next is not WaveMessage) Console.WriteLine($"[bridge] -> {next.Type} {json.Length}B");
                try
                {
                    await _webView.InvokeScript("window.tcb&&window.tcb.receive(" + json + ")");
                }
                catch (Exception ex)
                {
                    _ready = false;
                    _state.AppendLog($"Web への送信に失敗: {ex.Message}", false);
                    _reliable.Clear();
                    _latestClock = null;
                    _latestWave = null;
                    break;
                }
            }
        }
        finally
        {
            _sending = false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _webView.WebMessageReceived -= OnWebMessage;
        _state.Changed -= OnStateChanged;
    }
}
