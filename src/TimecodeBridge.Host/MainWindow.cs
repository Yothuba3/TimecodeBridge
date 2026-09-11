using Avalonia.Controls;
using Avalonia.Input;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.Host.Bridge;
using TimecodeBridge.Host.Services;

namespace TimecodeBridge.Host;

/// <summary>Web UI を全面に表示するだけのウィンドウ。操作はすべて <see cref="BridgeServer"/> 経由。</summary>
public sealed class MainWindow : Window
{
    private readonly BridgeServer _bridge;
    private readonly ITimecodeEngine _engine;

    public MainWindow(HostState state, CommandRouter router, ProjectCoordinator projects, ITimecodeEngine engine)
    {
        _engine = engine;
        projects.OwnerProvider = () => this;
        BuildNativeMenu(router);
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

    // macOS のメニューバー。Web 側のショートカット(Cmd+O/S/Z 等)は WebView 内では拾えないため、ここで受けて同じ command に流す
    private void BuildNativeMenu(CommandRouter router)
    {
        NativeMenuItem Item(string header, string command, Key key, KeyModifiers modifiers = KeyModifiers.Meta)
        {
            var item = new NativeMenuItem(header) { Gesture = new KeyGesture(key, modifiers) };
            item.Click += async (_, _) => await router.ExecuteAsync(CommandRouter.Synthetic("menu", command));
            return item;
        }

        var file = new NativeMenu();
        file.Items.Add(Item("新規プロジェクト", "project.new", Key.N));
        file.Items.Add(Item("開く…", "project.open", Key.O));
        file.Items.Add(new NativeMenuItemSeparator());
        file.Items.Add(Item("保存", "project.save", Key.S));
        file.Items.Add(Item("名前を付けて保存…", "project.saveAs", Key.S, KeyModifiers.Meta | KeyModifiers.Shift));

        var edit = new NativeMenu();
        edit.Items.Add(Item("取り消す", "app.undo", Key.Z));
        edit.Items.Add(Item("やり直す", "app.redo", Key.Z, KeyModifiers.Meta | KeyModifiers.Shift));

        var menu = new NativeMenu();
        menu.Items.Add(new NativeMenuItem("ファイル") { Menu = file });
        menu.Items.Add(new NativeMenuItem("編集") { Menu = edit });
        NativeMenu.SetMenu(this, menu);
    }
}
