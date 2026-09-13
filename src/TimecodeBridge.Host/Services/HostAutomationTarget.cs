using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using TimecodeBridge.Host.Bridge;

namespace TimecodeBridge.Host.Services;

/// <summary>実機の Window/WebView/HostState を <see cref="DevAutomation"/> に見せる。呼び出しはすべて UI スレッドへ移す。</summary>
public sealed class HostAutomationTarget : IAutomationTarget
{
    private readonly Window _window;
    private readonly NativeWebView _webView;
    private readonly HostState _state;
    private readonly CommandRouter _router;
    private readonly BridgeServer _bridge;

    public HostAutomationTarget(Window window, NativeWebView webView, HostState state, CommandRouter router, BridgeServer bridge)
    {
        _window = window; _webView = webView; _state = state; _router = router; _bridge = bridge;
    }

    public bool WebReady => _bridge.Ready;

    public Task<SnapshotMessage> SnapshotAsync() =>
        Dispatcher.UIThread.InvokeAsync(() => new SnapshotMessage(_state.Revision, _state.BuildSnapshot())).GetTask();

    public Task<ClockState> ClockAsync() =>
        Dispatcher.UIThread.InvokeAsync(() => _state.BuildClock()).GetTask();

    public Task<ResultMessage> ExecuteAsync(WebMessage message) =>
        Dispatcher.UIThread.InvokeAsync(() => _router.ExecuteAsync(message));

    public Task<string?> EvalAsync(string script) =>
        Dispatcher.UIThread.InvokeAsync(async () => (await _webView.InvokeScript(script))?.ToString());

    public Task ActivateAsync() =>
        Dispatcher.UIThread.InvokeAsync(() => _window.Activate()).GetTask();

    // Navigate や Refresh だと WebKit が assets/app.js をキャッシュから返すので、macOS では reloadFromOrigin を使う
    public Task ReloadWebAsync() =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (OperatingSystem.IsMacOS() && _webView.TryGetPlatformHandle() is IAppleWKWebViewPlatformHandle apple && apple.WKWebView != IntPtr.Zero)
                MacWebKit.ReloadFromOrigin(apple.WKWebView);
            else
                _webView.Navigate(new UriBuilder(WebAssets.IndexUri()) { Query = "r=" + Environment.TickCount64 }.Uri);
        }).GetTask();

    public Task<byte[]?> CaptureWindowAsync() =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!OperatingSystem.IsMacOS()) return null;
            if (_window.TryGetPlatformHandle() is not IMacOSTopLevelPlatformHandle mac) return null;
            return MacWindowCapture.CapturePng(mac.NSWindow);
        }).GetTask();
}
