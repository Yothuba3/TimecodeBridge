using System.Collections;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using TimecodeBridge.App.ViewModels;

namespace TimecodeBridge.App.Services.Automation;

/// <summary>
/// 実機確認の自動化用。環境変数 TIMECODEBRIDGE_AUTOMATION_PORT=&lt;port&gt; があれば 127.0.0.1:&lt;port&gt; で HTTP を待ち受け、
/// 状態の取得・ビジュアルツリーの列挙・クリック/入力/キー・ウィンドウの撮影を外部(tools/tcb2/tcb2ctl)から行えるようにする。
/// ループバックだけに束縛し、環境変数が無ければ何も起動しない。UI を触る処理はすべて UI スレッドへ移す。
/// </summary>
public sealed class DevAutomation : IDisposable
{
    public const string EnvironmentVariable = "TIMECODEBRIDGE_AUTOMATION_PORT";

    // 人が curl で読むので日本語をエスケープしない
    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        MaxDepth = 16,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly DateTime _startedAt = DateTime.UtcNow;
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly Func<MainViewModel?> _viewModel;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();

    public static DevAutomation? StartIfRequested(IClassicDesktopStyleApplicationLifetime desktop, Func<MainViewModel?> viewModel)
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
            var automation = new DevAutomation(desktop, viewModel, port);
            Console.WriteLine($"[automation] listening on {automation.BaseUrl}");
            return automation;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[automation] failed to listen on port {port}: {ex.Message}");
            return null;
        }
    }

    public DevAutomation(IClassicDesktopStyleApplicationLifetime desktop, Func<MainViewModel?> viewModel, int port)
    {
        _desktop = desktop;
        _viewModel = viewModel;
        BaseUrl = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(BaseUrl);
        _listener.Start();
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
                    await WriteJsonAsync(res, 200, new
                    {
                        ok = true,
                        pid = Environment.ProcessId,
                        ready = await OnUiAsync(() => _desktop.MainWindow?.IsVisible == true && _viewModel() is not null),
                        uptimeSeconds = (DateTime.UtcNow - _startedAt).TotalSeconds,
                    });
                    break;
                case ("GET", "/state"):
                    await WriteJsonAsync(res, 200, await OnUiAsync(Snapshot));
                    break;
                case ("GET", "/windows"):
                    await WriteJsonAsync(res, 200, await OnUiAsync(() => _desktop.Windows.Select(UiAutomation.DescribeWindow).ToList()));
                    break;
                case ("GET", "/tree"):
                {
                    var window = req.QueryString["window"];
                    var all = req.QueryString["all"] is "1" or "true";
                    await WriteJsonAsync(res, 200, await OnUiAsync(() =>
                    {
                        var w = UiAutomation.ResolveWindow(_desktop.Windows, window);
                        return new { window = w.Title, nodes = UiAutomation.Tree(w, all) };
                    }));
                    break;
                }
                case ("POST", "/find"):
                {
                    var selector = SelectorOf(await ReadJsonAsync(req));
                    await WriteJsonAsync(res, 200, await OnUiAsync(() =>
                    {
                        var w = UiAutomation.ResolveWindow(_desktop.Windows, selector.Window);
                        return new { window = w.Title, matches = UiAutomation.FindAll(w, selector).Select(x => UiAutomation.Describe(x.Control, x.Id, w)).ToList() };
                    }));
                    break;
                }
                case ("POST", "/click"):
                {
                    var body = await ReadJsonAsync(req);
                    var selector = SelectorOf(body);
                    var additive = body.TryGetProperty("additive", out var ad) && ad.ValueKind == JsonValueKind.True;
                    await WriteJsonAsync(res, 200, await ActAsync(selector, Wait(body), c => additive && UiAutomation.Clickable(c) is ListBoxItem item ? UiAutomation.SelectItem(item, additive: true) : UiAutomation.Click(c)));
                    break;
                }
                case ("POST", "/type"):
                {
                    var body = await ReadJsonAsync(req);
                    var selector = SelectorOf(body);
                    var text = body.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : throw new ArgumentException("text が必要です");
                    var commit = !(body.TryGetProperty("commit", out var cm) && cm.ValueKind == JsonValueKind.False);
                    await WriteJsonAsync(res, 200, await ActAsync(selector, Wait(body, defaultWait: true), c => UiAutomation.Type(c, text, commit)));
                    break;
                }
                case ("POST", "/set"):
                {
                    var body = await ReadJsonAsync(req);
                    var selector = SelectorOf(body);
                    await WriteJsonAsync(res, 200, await ActAsync(selector, Wait(body, defaultWait: true), c => UiAutomation.Set(c, body)));
                    break;
                }
                case ("POST", "/key"):
                {
                    var body = await ReadJsonAsync(req);
                    var keyName = body.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString()! : throw new ArgumentException("key が必要です");
                    if (!Enum.TryParse<Key>(keyName, true, out var key)) throw new ArgumentException($"不明なキー: {keyName}");
                    var modifiers = UiAutomation.ParseModifiers(body.TryGetProperty("modifiers", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null);
                    var window = body.TryGetProperty("window", out var wn) && wn.ValueKind == JsonValueKind.String ? wn.GetString() : null;
                    var wait = Wait(body);
                    var result = await ActOnUiAsync(wait, () => UiAutomation.SendKey(UiAutomation.ResolveWindow(_desktop.Windows, window), key, modifiers));
                    await WriteJsonAsync(res, 200, result);
                    break;
                }
                case ("POST", "/invoke"):
                {
                    var body = await ReadJsonAsync(req);
                    var pathText = body.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : throw new ArgumentException("path が必要です");
                    JsonElement? parameter = body.TryGetProperty("parameter", out var prm) && prm.ValueKind != JsonValueKind.Null ? prm.Clone() : null;
                    var result = await ActOnUiAsync(Wait(body), () => Invoke(pathText, parameter));
                    await WriteJsonAsync(res, 200, result);
                    break;
                }
                case ("POST", "/get"):
                case ("GET", "/get"):
                {
                    var pathText = req.HttpMethod == "GET"
                        ? req.QueryString["path"] ?? throw new ArgumentException("path が必要です")
                        : (await ReadJsonAsync(req)).GetProperty("path").GetString()!;
                    var value = await OnUiAsync(() => Resolve(pathText).Value);
                    await WriteJsonAsync(res, 200, new { path = pathText, value });
                    break;
                }
                case ("POST", "/activate"):
                {
                    var body = req.ContentLength64 > 0 ? await ReadJsonAsync(req) : default;
                    var window = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("window", out var wn) && wn.ValueKind == JsonValueKind.String ? wn.GetString() : null;
                    await OnUiAsync(() => { UiAutomation.ResolveWindow(_desktop.Windows, window).Activate(); return true; });
                    await WriteJsonAsync(res, 200, new { ok = true });
                    break;
                }
                case ("POST", "/close"):
                {
                    var body = req.ContentLength64 > 0 ? await ReadJsonAsync(req) : default;
                    var window = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("window", out var wn) && wn.ValueKind == JsonValueKind.String ? wn.GetString() : null;
                    var title = await OnUiAsync(() =>
                    {
                        var w = UiAutomation.ResolveWindow(_desktop.Windows, window);
                        if (w == _desktop.MainWindow) throw new InvalidOperationException("メインウィンドウは close では閉じません(stop を使ってください)");
                        w.Close();
                        return w.Title;
                    });
                    await WriteJsonAsync(res, 200, new { ok = true, closed = title });
                    break;
                }
                case ("GET", "/screenshot"):
                {
                    var window = req.QueryString["window"];
                    var png = await OnUiAsync(() => UiAutomation.Capture(UiAutomation.ResolveWindow(_desktop.Windows, window)));
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
            var inner = ex is TargetInvocationException { InnerException: { } ie } ? ie : ex;
            try { await WriteJsonAsync(res, inner is ArgumentException or JsonException or KeyNotFoundException ? 400 : 500, new { error = inner.Message, type = inner.GetType().Name }); }
            catch { /* 応答先が既に閉じている */ }
        }
        finally
        {
            try { res.Close(); } catch { /* 二重 close は無視 */ }
        }
    }

    /// <summary>操作の本文は {"selector":{...}, ...操作固有の値} 。入力する文字列(text)とセレクタの text を混同しないよう分けている。</summary>
    private static UiSelector SelectorOf(JsonElement body)
        => body.TryGetProperty("selector", out var s) ? UiSelector.Parse(s) : throw new ArgumentException("selector が必要です");

    private static bool Wait(JsonElement body, bool defaultWait = false)
        => body.TryGetProperty("wait", out var w) ? w.ValueKind == JsonValueKind.True : defaultWait;

    /// <summary>
    /// 要素を探してから操作する。モーダルダイアログを開く操作は閉じるまで戻ってこないので、
    /// 既定(wait=false)では UI スレッドへ投げてすぐ応答し、呼び出し側は /windows や /state で結果を見る。
    /// </summary>
    private async Task<object> ActAsync(UiSelector selector, bool wait, Func<Control, string> action)
    {
        var (control, node) = await OnUiAsync(() =>
        {
            var w = UiAutomation.ResolveWindow(_desktop.Windows, selector.Window);
            var found = UiAutomation.Find(w, selector);
            return (found.Control, UiAutomation.Describe(found.Control, found.Id, w));
        });
        var result = await ActOnUiAsync(wait, () => action(control));
        return new { ok = true, target = node, result };
    }

    private static async Task<object> ActOnUiAsync(bool wait, Func<string> action)
    {
        if (wait) return new { ok = true, posted = false, result = await Dispatcher.UIThread.InvokeAsync(action) };
        Dispatcher.UIThread.Post(() =>
        {
            try { action(); }
            catch (Exception ex) { Console.WriteLine($"[automation] posted action failed: {ex.Message}"); }
        });
        return new { ok = true, posted = true };
    }

    private static Task<T> OnUiAsync<T>(Func<T> func) => Dispatcher.UIThread.InvokeAsync(func).GetTask();

    /// <summary>"CueListViewModel.AddCueCommand" のように MainViewModel からのプロパティ経路を辿る。</summary>
    private (object? Value, object? Owner, PropertyInfo? Property) Resolve(string path)
    {
        object? current = _viewModel() ?? throw new InvalidOperationException("MainViewModel がまだありません");
        object? owner = null;
        PropertyInfo? property = null;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current is null) throw new KeyNotFoundException($"{path}: 途中で null になりました");
            if (segment.EndsWith(']') && segment.IndexOf('[') is var open and > 0 && current is IEnumerable)
            {
                // Cues[2] のような添字
                var listProp = current.GetType().GetProperty(segment[..open]) ?? throw new KeyNotFoundException($"{path}: {segment[..open]} がありません");
                var list = (listProp.GetValue(current) as IEnumerable ?? throw new KeyNotFoundException($"{path}: {segment} は列挙できません")).Cast<object?>().ToList();
                var index = int.Parse(segment[(open + 1)..^1]);
                owner = current; property = listProp; current = list[index];
                continue;
            }
            property = current.GetType().GetProperty(segment, BindingFlags.Instance | BindingFlags.Public)
                       ?? throw new KeyNotFoundException($"{path}: {current.GetType().Name} に {segment} がありません");
            owner = current;
            current = property.GetValue(current);
        }
        return (current, owner, property);
    }

    private string Invoke(string path, JsonElement? parameter)
    {
        var (value, owner, property) = Resolve(path);
        if (value is ICommand command)
        {
            var arg = ConvertParameter(parameter, typeof(object));
            if (!command.CanExecute(arg)) throw new InvalidOperationException($"{path} は今は実行できません(CanExecute=false)");
            command.Execute(arg);
            return $"execute {path}";
        }
        if (property is not null && owner is not null && parameter is not null && property.CanWrite)
        {
            property.SetValue(owner, ConvertParameter(parameter, property.PropertyType));
            return $"set {path} = {parameter}";
        }
        throw new InvalidOperationException($"{path} は ICommand でも書き込めるプロパティでもありません");
    }

    private static object? ConvertParameter(JsonElement? parameter, Type target)
    {
        if (parameter is not { } p || p.ValueKind == JsonValueKind.Null) return null;
        if (target == typeof(object))
            return p.ValueKind switch
            {
                JsonValueKind.String => p.GetString(),
                JsonValueKind.Number => p.TryGetInt32(out var i) ? i : p.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => p.GetRawText(),
            };
        return p.Deserialize(target, Json);
    }

    private object Snapshot()
    {
        var vm = _viewModel() ?? throw new InvalidOperationException("MainViewModel がまだありません");
        var tc = vm.TimecodeViewModel;
        var cues = vm.CueListViewModel;
        var panel = vm.OscTriggerPanelViewModel;
        var relay = vm.RelayViewModel;
        var logs = vm.LogViewModel.Logs;
        return new
        {
            project = new
            {
                title = vm.Title,
                displayName = vm.ProjectDisplayName,
                filePath = vm.ProjectFilePath,
                hasUnsavedChanges = vm.HasUnsavedChanges,
                canUndo = vm.CanUndo,
                canRedo = vm.CanRedo,
                recentProjects = vm.RecentProjects,
                autoMuteEnabled = vm.AutoMuteEnabled,
            },
            timecode = new
            {
                raw = tc.RawTimecodeDisplay,
                display = tc.OffsetTimecodeDisplay,
                status = tc.StatusText,
                summary = tc.SourceStatusSummary,
                isReceiving = tc.IsReceiving,
                source = tc.SelectedSource.ToString(),
                inputDevice = tc.SelectedDevice?.DisplayName,
                outputDevice = tc.SelectedOutputDevice?.DisplayName,
                inputDevices = tc.AudioDevices.Select(d => d.DisplayName).ToList(),
                outputDevices = tc.OutputDevices.Select(d => d.DisplayName).ToList(),
                generatorRunning = tc.IsGeneratorRunning,
                generatorStartTime = tc.GeneratorStartTime,
                generatorFrameRate = tc.GeneratorFrameRate.ToString(),
                outputVolume = tc.OutputVolumeLevel,
                ltcOutputActive = tc.IsLtcOutputActive,
                triggerMuted = tc.IsTriggerMuted,
                offset = tc.OffsetText,
                detectedFrameRate = tc.DetectedFrameRateText,
                signalDetail = tc.SignalDetailText,
                signalErrorRate = tc.SignalErrorRateText,
            },
            cues = cues.CueItems.Select(c => new
            {
                id = c.Id,
                name = c.Name,
                triggerTime = c.TriggerTime.ToString(),
                oscAddress = c.OscAddress,
                oscAddressDisplay = c.OscAddressDisplay,
                isEnabled = c.IsEnabled,
                isTriggered = c.IsTriggered,
                isNextCue = c.IsNextCue,
                muteStatusLabel = c.MuteStatusLabel,
                muteCountdownText = c.MuteCountdownText,
                memo = c.Memo,
            }).ToList(),
            nextCueSummary = cues.NextCueSummary,
            triggerWindowFrames = cues.TriggerWindowFrames,
            hosts = vm.HostManagerViewModel.Hosts.ToList(),
            cueSync = new
            {
                oscAddress = tc.CueSync.OscAddress,
                targetHostIds = tc.CueSync.TargetHostIds,
                hostSelections = tc.CueSync.HostSelections.Select(h => new { h.Id, h.Name, h.IsSelected }).ToList(),
            },
            relay = new
            {
                oscAddressPattern = relay.OscAddressPattern,
                isContinuousEnabled = relay.IsContinuousEnabled,
                targetHostIds = relay.TargetHostIds,
                intervalMs = relay.IntervalMs,
                intervalModeIndex = relay.IntervalModeIndex,
            },
            triggerPanel = new
            {
                rows = panel.Rows,
                columns = panel.Columns,
                isEditMode = panel.IsEditMode,
                cells = panel.Cells.Select(c => new { c.Row, c.Column, c.IsConfigured, c.DisplayText, c.IsFlashing, button = c.Button }).ToList(),
            },
            logs = logs.Skip(Math.Max(0, logs.Count - 100)).ToList(),
            windows = _desktop.Windows.Select(UiAutomation.DescribeWindow).ToList(),
        };
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpListenerRequest req)
    {
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(body)) throw new ArgumentException("JSON の本文が必要です");
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("JSON オブジェクトが必要です");
        return doc.RootElement.Clone();
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
