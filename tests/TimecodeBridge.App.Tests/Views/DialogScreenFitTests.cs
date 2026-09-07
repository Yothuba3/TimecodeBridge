using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TimecodeBridge.App.Views.Dialogs;
using TimecodeBridge.Core.Models;

namespace TimecodeBridge.App.Tests.Views;

public class DialogScreenFitTests
{
    [Fact]
    public void AvailableHeight_DividesByScalingAndLeavesRoomForFrame()
    {
        // 1080p を 125% 表示: 作業領域 1040px = 論理 832px、そこからタイトルバー等のぶんを引く
        var height = DialogScreenFit.AvailableHeight(new PixelRect(0, 0, 1920, 1040), 1.25);

        Assert.Equal(832 - 72, height);
    }

    [Fact]
    public void AvailableHeight_ReturnsNullWhenScreenInfoIsUnusable()
    {
        Assert.Null(DialogScreenFit.AvailableHeight(new PixelRect(0, 0, 800, 200), 1.0));
        Assert.Null(DialogScreenFit.AvailableHeight(new PixelRect(0, 0, 1920, 1080), 0));
    }

    [AvaloniaFact]
    public void CueEditDialog_ClampedBelowContentHeight_KeepsOkButtonInsideWindowAndScrollsForm()
        => AssertButtonsStayVisibleWhenClamped(new CueEditDialog());

    [AvaloniaFact]
    public void CueBatchEditDialog_ClampedBelowContentHeight_KeepsOkButtonInsideWindowAndScrollsForm()
        => AssertButtonsStayVisibleWhenClamped(new CueBatchEditDialog(3, [], FrameRate.Fps30));

    private static void AssertButtonsStayVisibleWhenClamped(Window dialog)
    {
        const double maxHeight = 400;
        dialog.MaxHeight = maxHeight;
        dialog.Show();
        try
        {
            dialog.UpdateLayout();
            var ok = dialog.FindControl<Button>("OkButton");
            var scroll = dialog.FindControl<ScrollViewer>("FormScroll");
            Assert.NotNull(ok);
            Assert.NotNull(scroll);

            Assert.True(dialog.Bounds.Height <= maxHeight + 0.5, $"ウィンドウ高さ {dialog.Bounds.Height} が上限を超えている");

            var okBottom = ok.TranslatePoint(new Point(0, ok.Bounds.Height), dialog)!.Value.Y;
            Assert.True(okBottom <= dialog.Bounds.Height + 0.5, $"OKボタン下端 {okBottom} がウィンドウ {dialog.Bounds.Height} の外");

            Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 0.5, "上限で縮めたときはフォーム側がスクロールするべき");
        }
        finally
        {
            dialog.Close();
        }
    }
}
