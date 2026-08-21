namespace TimecodeBridge.Models;

public class Cue
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string Memo { get; set; } = string.Empty;
    public required TimecodeValue TriggerTime { get; set; }
    public required string OscAddress { get; set; }

    /// <summary>
    /// 2個目以降のOSCアドレス。発火時にメインアドレスに続けて送信される。
    /// 一括編集を成立させるため、常に引数なしで送る制約を持つ。
    /// </summary>
    public List<string> AdditionalOscAddresses { get; set; } = [];

    public List<OscArgument> Arguments { get; set; } = [];
    public List<string> TargetHostIds { get; set; } = [];
    public bool IsEnabled { get; set; } = true;
    public bool SendTriggerTimeAsSeconds { get; set; }

    /// <summary>
    /// 秒数送信（<see cref="SendTriggerTimeAsSeconds"/>）で送るタイムコード。
    /// 未指定（null）ならトリガー時間をそのまま送る。
    /// </summary>
    public TimecodeValue? SendTimecode { get; set; }

    /// <summary>発火タイミングを±ずらすオフセット。実際の発火時刻はトリガー時間＋この値。</summary>
    public TimecodeOffset? TriggerOffset { get; set; }

    /// <summary>
    /// 旧形式（送信秒数の補正オフセット）。プロジェクト読込時に
    /// <see cref="SendTimecode"/> へ変換され、以後は使われない。
    /// </summary>
    public TimecodeOffset? CueOffset { get; set; }

    /// <summary>
    /// トリガーオフセット適用後の実際の発火時刻。
    /// 適用結果が0〜24時の範囲外になる場合はトリガー時間へフォールバックする
    /// （範囲外はダイアログ側の検証で弾かれるため、通常ここには来ない）。
    /// </summary>
    public TimecodeValue GetEffectiveTriggerTime()
        => TryApplyTriggerOffset(TriggerTime, TriggerOffset, out var effective) ? effective : TriggerTime;

    /// <summary>
    /// トリガー時間にオフセットを適用した実効発火時刻を求める。
    /// オフセットはトリガー時間側のフレームレートへ正規化して加算する
    /// （FPSが異なると「+1秒」のフレーム数がズレるため）。
    /// 結果が 00:00:00:00 未満または24時以上になる場合は false を返す。
    /// </summary>
    public static bool TryApplyTriggerOffset(TimecodeValue triggerTime, TimecodeOffset? offset, out TimecodeValue effective)
    {
        if (offset is not { } o)
        {
            effective = triggerTime;
            return true;
        }

        var fps = triggerTime.FrameRate.FramesPerSecond();
        var normalized = o.FrameRate == triggerTime.FrameRate
            ? o
            : new TimecodeOffset(o.IsNegative, o.Hours, o.Minutes, o.Seconds,
                Math.Min(o.Frames, fps - 1), triggerTime.FrameRate);

        long resultFrames = triggerTime.TotalFrames() + normalized.TotalFrames();
        long framesPerDay = 24L * 3600 * fps; // DFでは僅かに大きい近似だが範囲判定には十分

        if (resultFrames < 0 || resultFrames >= framesPerDay)
        {
            effective = triggerTime;
            return false;
        }

        effective = TimecodeValue.FromTotalFrames(resultFrames, triggerTime.FrameRate);
        return true;
    }
}
