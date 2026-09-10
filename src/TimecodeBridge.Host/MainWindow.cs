using Avalonia.Controls;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.Host.Bridge;
using TimecodeBridge.Host.Services;

namespace TimecodeBridge.Host;

/// <summary>Web UI を全面に表示するだけのウィンドウ。操作はすべて <see cref="BridgeServer"/> 経由。</summary>
public sealed class MainWindow : Window
{
    private readonly BridgeServer _bridge;
    private readonly ITimecodeEngine _engine;

    public MainWindow(HostState state, CommandRouter router, ITimecodeEngine engine)
    {
        _engine = engine;
        Title = "TimecodeBridge";
        Width = 1600;
        Height = 900;
        MinWidth = 1100;
        MinHeight = 640;

        var webView = new NativeWebView();
        Content = webView;
        _bridge = new BridgeServer(webView, state, router, Close);

        Opened += (_, _) => webView.Navigate(WebAssets.IndexUri());
        Closing += (_, _) =>
        {
            _bridge.Dispose();
            _engine.Stop();
        };
    }
}
