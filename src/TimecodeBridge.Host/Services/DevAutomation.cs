using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TimecodeBridge.Host.Bridge;

namespace TimecodeBridge.Host.Services;

/// <summary>自動操作の対象。実機では <see cref="HostAutomationTarget"/>、テストではフェイクを渡す。</summary>
public interface IAutomationTarget
{
    bool WebReady { get; }
    Task<SnapshotMessage> SnapshotAsync();
    Task<ClockState> ClockAsync();
    Task<ResultMessage> ExecuteAsync(WebMessage message);
    Task<string?> EvalAsync(string script);
    /// <summary>ウィンドウを PNG で撮る。撮れない環境では null。</summary>
    Task<byte[]?> CaptureWindowAsync();
    /// <summary>ウィンドウを前面に出す。隠れていると WebKit が描画と requestAnimationFrame を止めるため、確認の前に呼ぶ。</summary>
    Task ActivateAsync();
    /// <summary>Web UI を読み込み直す(TIMECODEBRIDGE_WEB_DIST を指していれば dist の変更がそのまま反映される)。Host の状態は保つ。</summary>
    Task ReloadWebAsync();
}

/// <summary>
/// 実機確認の自動化用。環境変数 TIMECODEBRIDGE_AUTOMATION_PORT=&lt;port&gt; があれば 127.0.0.1:&lt;port&gt; で HTTP を待ち受け、
/// 状態の取得・command の実行・WebView 内での JS 実行・ウィンドウの撮影を外部(tools/tcb3/tcb3ctl)から行えるようにする。
/// ループバックだけに束縛し、環境変数が無ければ何も起動しない。
/// </summary>
public sealed class DevAutomation : IDisposable
{
    public const string EnvironmentVariable = "TIMECODEBRIDGE_AUTOMATION_PORT";

    // 人が curl で読むので日本語をエスケープしない(Web への送信とは別の設定)
    private static readonly JsonSerializerOptions Json = new(Protocol.Json) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly DateTime _startedAt = DateTime.UtcNow;
    private readonly IAutomationTarget _target;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private int _requestSeq;

    public static DevAutomation? StartIfRequested(IAutomationTarget target)
    {
        var text = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!int.TryParse(text, out var port) || port is < 1 or > 65535)
        {
            Console.WriteLine($"[automation] invalid port '{text}'");
            return null;
        }
        try
        {
            var automation = new DevAutomation(target, port);
            Console.WriteLine($"[automation] listening on {automation.BaseUrl}");
            return automation;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[automation] failed to listen on port {port}: {ex.Message}");
            return null;
        }
    }

    public DevAutomation(IAutomationTarget target, int port)
    {
        _target = target;
        BaseUrl = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(BaseUrl);
        _listener.Start();
        // UI スレッドで生成されるので、待受と応答は thread pool に逃がす(対象側が必要に応じて UI スレッドへ移す)
        _ = Task.Run(LoopAsync);
    }

    public string BaseUrl { get; }

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch (Exception) when (_cts.IsCancellationRequested) { return; }
            catch (Exception ex) { Console.WriteLine($"[automation] accept failed: {ex.Message}"); continue; }
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        try
        {
            var path = req.Url?.AbsolutePath ?? "/";
            switch (req.HttpMethod, path)
            {
                case ("GET", "/health"):
                    await WriteJsonAsync(res, 200, new { ok = true, pid = Environment.ProcessId, webReady = _target.WebReady, uptimeSeconds = (DateTime.UtcNow - _startedAt).TotalSeconds });
                    break;
                case ("GET", "/state"):
                    await WriteJsonAsync(res, 200, await _target.SnapshotAsync());
                    break;
                case ("GET", "/clock"):
                    await WriteJsonAsync(res, 200, await _target.ClockAsync());
                    break;
                case ("POST", "/command"):
                {
                    var body = await ReadBodyAsync(req);
                    WebMessage message;
                    try { message = ParseCommand(body); }
                    catch (Exception ex) { await WriteJsonAsync(res, 400, new { error = ex.Message }); break; }
                    await WriteJsonAsync(res, 200, await _target.ExecuteAsync(message));
                    break;
                }
                case ("POST", "/eval"):
                {
                    var script = await ReadBodyAsync(req);
                    if (script.Trim().Length == 0) { await WriteJsonAsync(res, 400, new { error = "script が空です" }); break; }
                    await WriteJsonAsync(res, 200, new { result = await _target.EvalAsync(script) });
                    break;
                }
                case ("POST", "/activate"):
                    await _target.ActivateAsync();
                    await WriteJsonAsync(res, 200, new { ok = true });
                    break;
                case ("POST", "/reload"):
                    await _target.ReloadWebAsync();
                    await WriteJsonAsync(res, 200, new { ok = true });
                    break;
                case ("GET", "/screenshot"):
                {
                    var png = await _target.CaptureWindowAsync();
                    if (png is null) { await WriteJsonAsync(res, 501, new { error = "この環境ではウィンドウを撮影できません" }); break; }
                    res.StatusCode = 200;
                    res.ContentType = "image/png";
                    res.ContentLength64 = png.Length;
                    await res.OutputStream.WriteAsync(png);
                    break;
                }
                default:
                    await WriteJsonAsync(res, 404, new { error = $"未知のエンドポイント: {req.HttpMethod} {path}" });
                    break;
            }
        }
        catch (Exception ex)
        {
            try { await WriteJsonAsync(res, 500, new { error = ex.Message }); } catch { /* 応答先が既に閉じている */ }
        }
        finally
        {
            try { res.Close(); } catch { /* 二重 close は無視 */ }
        }
    }

    /// <summary>{"command":"cue.add","args":{...},"expectedRevision":12} を Web からの command と同じ形にする。</summary>
    private WebMessage ParseCommand(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("JSON オブジェクトが必要です");
        var command = root.TryGetProperty("command", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        if (string.IsNullOrEmpty(command)) throw new JsonException("command がありません");
        JsonElement? args = root.TryGetProperty("args", out var a) && a.ValueKind != JsonValueKind.Null ? a.Clone() : null;
        long? expected = root.TryGetProperty("expectedRevision", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt64() : null;
        var requestId = root.TryGetProperty("requestId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()! : $"auto-{Interlocked.Increment(ref _requestSeq)}";
        return new WebMessage(Protocol.Version, "command", requestId, command, args, expected, null, null, null, null, null, null, null);
    }

    private static async Task<string> ReadBodyAsync(HttpListenerRequest req)
    {
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static async Task WriteJsonAsync(HttpListenerResponse res, int status, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType(), Json);
        res.StatusCode = status;
        res.ContentType = "application/json; charset=utf-8";
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); _listener.Close(); } catch { /* 停止済み */ }
    }
}
