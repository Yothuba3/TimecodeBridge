using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Core.Services.Interfaces;

public interface ICueManager
{
    IReadOnlyList<Cue> Cues { get; }

    /// <summary>
    /// Trigger window size in frames. A cue fires when timecode enters
    /// [TriggerTime, TriggerTime + TriggerWindowFrames) and is suppressed
    /// until timecode leaves this window.
    /// </summary>
    int TriggerWindowFrames { get; set; }

    /// <summary>
    /// When true, timecode-triggered cues are suppressed. Manual triggers still work.
    /// </summary>
    bool IsMuted { get; set; }

    /// <summary>
    /// 発火時オートミュート機能のマスタースイッチ。falseなら各キューの
    /// <see cref="Cue.AutoMuteOnFire"/> 設定を無視する。プロジェクト単位で永続化される。
    /// </summary>
    bool IsAutoMuteEnabled { get; set; }

    /// <summary>
    /// <see cref="IsMuted"/> が変化したとき（オートミュート・自動解除・手動を問わず）発火する。
    /// 呼び出し元スレッドはワーカー/タイマーの場合があるためUIへの反映側でマーシャリングすること。
    /// </summary>
    event EventHandler MuteStateChanged;

    /// <summary>発火時オートミュートの原因となったキューID。オートミュート中でなければ null。</summary>
    string? AutoMutedCueId { get; }

    /// <summary>オートミュートの自動解除予定時刻（UTC）。時限解除でなければ null。</summary>
    DateTime? AutoUnmuteAt { get; }

    void AddCue(Cue cue);
    void UpdateCue(string cueId, Cue updatedCue);
    void RemoveCue(string cueId);
    void ReorderCues(IReadOnlyList<string> orderedCueIds);
    void SetCueEnabled(string cueId, bool enabled);
    void ManualTrigger(string cueId);

    /// <summary>再生位置の追跡状態を仕切り直す（位置ジャンプ時の中間キュー一斉発火を防ぐ）。</summary>
    void ResetTracking();

    /// <summary>
    /// Cue-Syncワンショット送信。現在のオフセット後TCに対し、実効発火時刻が直前の
    /// 有効なキューを基準に「送信タイムコード + (現在TC - 実効発火時刻)」を秒数(float)で送信する。
    /// 送信タイムコード未指定のキューはトリガー時間を送信軸とみなす。
    /// 基準キューがなければ 0.0 を送る。
    /// </summary>
    void SendCueSync(string oscAddress, IReadOnlyList<string> targetHostIds);

    event EventHandler<CueTriggeredEventArgs> CueTriggered;
}

public class CueTriggeredEventArgs : EventArgs
{
    public required Cue Cue { get; init; }
    public required TimecodeValue TriggerTimecode { get; init; }
    public required bool IsManual { get; init; }
}
