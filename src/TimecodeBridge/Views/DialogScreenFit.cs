using System.Windows;

namespace TimecodeBridge.Views;

/// <summary>
/// 行数の多い固定高ダイアログは、低解像度やDPI拡大(125〜150%)のWindowsで画面より背が高くなり、
/// 下端のOK/キャンセルが画面外へ出て押せなくなる。作業領域に収まる高さの上限を返す。
/// </summary>
public static class DialogScreenFit
{
    // CenterOwner で置いたときに画面端へ張り付かないための余白（論理px）
    private const double Margin = 48;

    // これより低い上限は実用にならない
    private const double MinUsableHeight = 320;

    /// <summary>ponytail: プライマリ画面の作業領域基準。DPIの異なるサブ画面へ出す運用が出たら所有ウィンドウの画面を見る。</summary>
    public static double MaxHeightForWorkArea() =>
        MaxHeightFor(SystemParameters.WorkArea.Height);

    public static double MaxHeightFor(double workAreaHeight) =>
        System.Math.Max(MinUsableHeight, workAreaHeight - Margin);
}
