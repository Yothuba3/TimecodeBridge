using Avalonia.Controls;
using Avalonia.Input;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.Host.Bridge;
using TimecodeBridge.Host.Services;

namespace TimecodeBridge.Host;

/// <summary>Web UI を全面に表示するだけのウィンドウ。操作はすべて <see cref="BridgeServer"/> 経由。</summary>
public sealed class MainWindow : Window
{
    private readonly BridgeServer? _bridge;
    private readonly DevAutomation? _automation;
    private readonly ITimecodeEngine _engine;

    public MainWindow(HostState state, CommandRouter router, ProjectCoordinator projects, ITimecodeEngine engine, RecentProjectsStore recent)
    {
        _engine = engine;
        projects.OwnerProvider = () => this;
        BuildNativeMenu(router, recent);
        recent.Changed += () => BuildNativeMenu(router, recent);
        Title = "TimecodeBridge";
        Width = 1600;
        Height = 900;
        MinWidth = 1100;
        MinHeight = 640;

        NativeWebView webView;
        try
        {
            webView = new NativeWebView();
        }
        catch (Exception ex)
        {
            // Windows では WebView2 ランタイム(Evergreen)が無いと生成に失敗する。原因と入手先を示して止める
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Margin = new Avalonia.Thickness(16),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Text = "画面の表示に使う WebView を初期化できませんでした。\n\n" +
                           (OperatingSystem.IsWindows()
                               ? "Windows では Microsoft Edge WebView2 ランタイムが必要です。https://developer.microsoft.com/microsoft-edge/webview2/ から Evergreen ランタイムをインストールして再起動してください。\n\n"
                               : "") + ex,
                },
            };
            _bridge = null!;
            return;
        }
        Content = webView;
        _bridge = new BridgeServer(webView, state, router, Close);
        _automation = DevAutomation.StartIfRequested(new HostAutomationTarget(this, webView, state, router, _bridge));

        Opened += (_, _) => webView.Navigate(WebAssets.IndexUri());
        Closing += (_, _) =>
        {
            _automation?.Dispose();
            _bridge?.Dispose();
            _engine.Stop();
        };
    }

    // macOS のメニューバー。Web 側のショートカット(Cmd+O/S/Z 等)は WebView 内では拾えないため、ここで受けて同じ command に流す
    private void BuildNativeMenu(CommandRouter router, RecentProjectsStore recent)
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
        var recentMenu = new NativeMenu();
        foreach (var path in recent.Items)
        {
            var entry = new NativeMenuItem(Path.GetFileName(path)) { ToolTip = path };
            entry.Click += async (_, _) => await router.ExecuteAsync(CommandRouter.Synthetic("menu", "project.open", new { path }));
            recentMenu.Items.Add(entry);
        }
        file.Items.Add(new NativeMenuItem("最近使ったプロジェクト") { Menu = recentMenu, IsEnabled = recent.Items.Count > 0 });
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
