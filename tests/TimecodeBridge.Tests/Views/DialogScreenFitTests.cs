using System.Windows;
using System.Windows.Controls;
using TimecodeBridge.Models;
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

    // Application はプロセスに1つ・作成スレッドに固定されるので、2ダイアログを1テストにまとめる
    [StaFact]
    public void 高さ上限で縮めてもOKボタンが画面内に残りフォームがスクロールする()
    {
        EnsureAppResources();

        var cue = new Cue
        {
            Id = "test-cue",
            Name = "test",
            OscAddress = "/test",
            TriggerTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30),
        };
        AssertOkStaysVisible(new CueEditDialog(cue, [], FrameRate.Fps30));
        AssertOkStaysVisible(new CueBatchEditDialog(3, [], FrameRate.Fps30));
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

    private static void AssertOkStaysVisible(Window dialog)
    {
        const double maxHeight = 400;
        dialog.MaxHeight = maxHeight;
        dialog.ShowInTaskbar = false;
        dialog.Show();
        try
        {
            dialog.UpdateLayout();

            var ok = (FrameworkElement)dialog.FindName("OkButton")!;
            var scroll = (ScrollViewer)dialog.FindName("FormScroll")!;

            Assert.True(dialog.ActualHeight <= maxHeight + 0.5,
                $"{dialog.GetType().Name}: ウィンドウ高さ {dialog.ActualHeight} が上限 {maxHeight} を超えている");

            var okBottom = ok.TransformToAncestor(dialog).Transform(new Point(0, ok.ActualHeight)).Y;
            Assert.True(okBottom <= dialog.ActualHeight + 0.5,
                $"{dialog.GetType().Name}: OKボタン下端 {okBottom} がウィンドウ {dialog.ActualHeight} の外にある");

            Assert.True(scroll.ExtentHeight > scroll.ViewportHeight + 0.5,
                $"{dialog.GetType().Name}: 上限で縮めたときはフォーム側がスクロールするべき");
        }
        finally
        {
            dialog.Close();
        }
    }
}
