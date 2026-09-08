using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TimecodeBridge.Models;
using TimecodeBridge;
using TimecodeBridge.Views;

namespace TimecodeBridge.Tests.Views;

public class DialogScreenFitTests
{
    [Fact]
    public void MaxHeightFor_作業領域から余白を引いた高さを返す()
    {
        Assert.Equal(1040 - 48, DialogScreenFit.MaxHeightFor(1040));
    }

    [Fact]
    public void MaxHeightFor_作業領域が極端に低くても実用下限を下回らない()
    {
        Assert.Equal(320, DialogScreenFit.MaxHeightFor(100));
    }

    [StaFact]
    public void ウィンドウが画面より小さくても中身はスクロールで届き操作ボタンが残る()
    {
        EnsureAppResources();

        var cue = new Cue
        {
            Id = "test-cue",
            Name = "test",
            OscAddress = "/test",
            TriggerTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30),
        };
        AssertOkStaysVisible(new CueEditDialog(cue, [], FrameRate.Fps30), expectFormScroll: true);
        AssertOkStaysVisible(new CueBatchEditDialog(3, [], FrameRate.Fps30), expectFormScroll: true);

        var host = new OscHost { Id = "h", Name = "h", IpAddress = "127.0.0.1", Port = 9000 };
        AssertOkStaysVisible(new OscTriggerButtonEditDialog(
            new OscTriggerButton { Id = "b", Row = 0, Column = 0 }, [host], canDelete: true),
            expectFormScroll: true);
        AssertOkStaysVisible(new HostEditDialog(host), expectFormScroll: true);
        AssertOkStaysVisible(new BatchDuplicateDialog(), expectFormScroll: true);

        AssertMainWindowScrolls();
    }

    // WPF の Application はプロセスに1つ・作成スレッド固定。StaFact はテストごとに別スレッドを作るので、
    // UIを触る検証はこの1メソッドにまとめる。
    private static void AssertMainWindowScrolls()
    {
        // 既定サイズより画面が小さくても、ウィンドウ自体は作業領域に収まる
        var probe = new MainWindow();
        Assert.True(probe.MaxWidth <= SystemParameters.WorkArea.Width,
            "ウィンドウ幅の上限が作業領域を超えている");
        Assert.True(probe.MaxHeight <= SystemParameters.WorkArea.Height,
            "ウィンドウ高さの上限が作業領域を超えている");

        var window = new MainWindow { Width = 640, Height = 400, ShowInTaskbar = false };
        window.Show();
        try
        {
            window.UpdateLayout();

            var scroll = (ScrollViewer)window.FindName("MainScroll")!;
            Assert.True(scroll.ExtentHeight > scroll.ViewportHeight + 0.5,
                "画面より中身が大きいときは縦スクロールできるべき");
            Assert.True(scroll.ExtentWidth > scroll.ViewportWidth + 0.5,
                "画面より中身が広いときは横スクロールできるべき");

            // ステータスバーはスクロール領域の外なので常に見えている
            var badge = (FrameworkElement)window.FindName("StatusSourceText")!;
            var bottom = badge.TransformToAncestor(window).Transform(new Point(0, badge.ActualHeight)).Y;
            Assert.True(bottom <= window.ActualHeight + 0.5,
                $"ステータスバー下端 {bottom} がウィンドウ {window.ActualHeight} の外にある");
        }
        finally
        {
            window.Close();
        }

        // 通常サイズでは従来どおり画面いっぱいに広がる（スクロールは出ない）
        // 作業領域より大きく開いて広い場合の挙動だけを見る（画面に収める上限は上で別に検証済み）
        var wide = new MainWindow
        {
            Width = 1600,
            Height = 840,
            MaxWidth = double.PositiveInfinity,
            MaxHeight = double.PositiveInfinity,
            ShowInTaskbar = false,
        };
        wide.Show();
        try
        {
            wide.UpdateLayout();
            var scroll = (ScrollViewer)wide.FindName("MainScroll")!;
            Assert.True(scroll.ExtentHeight <= scroll.ViewportHeight + 0.5,
                $"通常サイズで縦スクロールが出ている (extent {scroll.ExtentHeight} > viewport {scroll.ViewportHeight})");
            Assert.True(scroll.ExtentWidth <= scroll.ViewportWidth + 0.5,
                $"通常サイズで横スクロールが出ている (extent {scroll.ExtentWidth} > viewport {scroll.ViewportWidth})");
        }
        finally
        {
            wide.Close();
        }
    }

    // ダイアログの StaticResource は App.xaml のテーマ辞書にある。
    // 既定の ShutdownMode(OnLastWindowClose) だと1つ目を閉じた時点で以降がレイアウトされない。
    private static void EnsureAppResources()
    {
        if (Application.Current is null) _ = new Application();
        Application.Current!.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var resources = Application.Current.Resources;
        if (resources.Contains("AccentButtonStyle")) return;
        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/TimecodeBridge;component/Themes/DarkTheme.xaml",
                UriKind.Absolute),
        });
    }

    private static void AssertOkStaysVisible(Window dialog, bool expectFormScroll = false)
    {
        // どのダイアログの中身より低い値にして、必ずスクロールが要る状態を作る
        const double maxHeight = 200;
        dialog.MaxHeight = maxHeight;
        dialog.ShowInTaskbar = false;
        dialog.Show();
        try
        {
            dialog.UpdateLayout();

            var ok = (FrameworkElement)dialog.FindName("OkButton")!;

            Assert.True(dialog.ActualHeight <= maxHeight + 0.5,
                $"{dialog.GetType().Name}: ウィンドウ高さ {dialog.ActualHeight} が上限 {maxHeight} を超えている");

            var okBottom = ok.TransformToAncestor(dialog).Transform(new Point(0, ok.ActualHeight)).Y;
            Assert.True(okBottom <= dialog.ActualHeight + 0.5,
                $"{dialog.GetType().Name}: OKボタン下端 {okBottom} がウィンドウ {dialog.ActualHeight} の外にある");

            if (!expectFormScroll) return;
            var scroll = (ScrollViewer)dialog.FindName("FormScroll")!;
            Assert.True(scroll.ExtentHeight > scroll.ViewportHeight + 0.5,
                $"{dialog.GetType().Name}: 上限で縮めたときはフォーム側がスクロールするべき");

            // 既定(17px)ではなくテーマの細いスクロールバーが当たっている
            var bar = (ScrollBar)scroll.Template.FindName("PART_VerticalScrollBar", scroll)!;
            Assert.InRange(bar.ActualWidth, 1, 8);
        }
        finally
        {
            dialog.Close();
        }
    }
}
