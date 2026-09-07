using Avalonia;
using Avalonia.Controls;
using TimecodeBridge.App.Services;

namespace TimecodeBridge.App.Views.Dialogs;

/// <summary>
/// SizeToContent=Height のダイアログの高さを、表示先画面の作業領域に収める。
/// 行数の多いダイアログは、低解像度やDPI拡大(125〜150%)のWindowsで画面より背が高くなり、
/// 下端のOK/キャンセルが画面外へ出て押せなくなる。上限を付けて中身側をスクロールさせる。
/// </summary>
internal static class DialogScreenFit
{
    // OSのタイトルバー・ウィンドウ枠と、画面端に残す余白のぶん（論理px）
    private const double FrameAllowance = 72;

    // これより低い上限は実用にならないので、画面情報が異常なときは制限しない
    private const double MinUsableHeight = 240;

    public static void ClampHeightToOwnerScreen(Window dialog)
    {
        var owner = ModalDialog.MainWindow;
        var screen = owner is null ? null : dialog.Screens.ScreenFromWindow(owner);
        screen ??= dialog.Screens.Primary;
        if (screen is null) return;

        var available = AvailableHeight(screen.WorkingArea, screen.Scaling);
        if (available is { } h) dialog.MaxHeight = h;
    }

    /// <summary>作業領域（物理px）とスケーリングから、ダイアログに許す論理高さを求める。</summary>
    internal static double? AvailableHeight(PixelRect workingArea, double scaling)
    {
        if (scaling <= 0) return null;
        double available = workingArea.Height / scaling - FrameAllowance;
        return available >= MinUsableHeight ? available : null;
    }
}
