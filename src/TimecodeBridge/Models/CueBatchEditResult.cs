namespace TimecodeBridge.Models;

/// <summary>
/// 一括編集で変更されるフィールドを表す。nullのフィールドは変更しない。
/// </summary>
public class CueBatchEditResult
{
    public string? OscAddress { get; set; }

    /// <summary>2個目以降のOSCアドレス（引数なし送信）。null=変更しない、空リスト=全解除。</summary>
    public List<string>? AdditionalOscAddresses { get; set; }

    public List<OscArgument>? Arguments { get; set; }
    public List<string>? TargetHostIds { get; set; }
    public bool? IsEnabled { get; set; }
    public bool? SendTriggerTimeAsSeconds { get; set; }

    /// <summary>true = 送信タイムコードを適用（値がnullなら解除）。</summary>
    public bool ApplySendTimecode { get; set; }
    public TimecodeValue? SendTimecode { get; set; }

    /// <summary>
    /// true = トリガーオフセット値を適用, false = 変更しない。
    /// ApplyTriggerOffset が true のとき TriggerOffset の値（nullなら解除）を適用する。
    /// </summary>
    public bool ApplyTriggerOffset { get; set; }
    public TimecodeOffset? TriggerOffset { get; set; }

    public bool ApplyMemo { get; set; }
    public string? Memo { get; set; }
}
