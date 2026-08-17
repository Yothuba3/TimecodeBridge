using TimecodeBridge.Models;

namespace TimecodeBridge.Services.Interfaces;

/// <summary>
/// キュー関連のダイアログ表示を担当するサービス
/// </summary>
public interface ICueDialogService
{
    Cue? ShowEditDialog(Cue template, IReadOnlyList<OscHost> hosts, FrameRate frameRate, string title);
    CueBatchEditResult? ShowBatchEditDialog(int cueCount, IReadOnlyList<OscHost> hosts, FrameRate frameRate);
    /// <summary>連続複製ダイアログ。間隔は時:分:秒で指定される。</summary>
    (int Count, TimeSpan Interval)? ShowBatchDuplicateDialog();
}
