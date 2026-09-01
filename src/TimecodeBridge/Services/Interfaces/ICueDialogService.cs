using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Services.Interfaces;

/// <summary>
/// キュー編集ダイアログサービス
/// </summary>
public interface ICueDialogService
{
    /// <summary>
    /// キュー編集ダイアログを表示する
    /// </summary>
    Cue? ShowEditDialog(Cue template, IReadOnlyList<OscHost> hosts, FrameRate frameRate, string title);

    /// <summary>
    /// 一括編集ダイアログを表示する
    /// </summary>
    CueBatchEditResult? ShowBatchEditDialog(int cueCount, IReadOnlyList<OscHost> hosts, FrameRate frameRate);

    /// <summary>
    /// 一括複製ダイアログを表示する
    /// </summary>
    (int Count, int IntervalHours)? ShowBatchDuplicateDialog();
}
