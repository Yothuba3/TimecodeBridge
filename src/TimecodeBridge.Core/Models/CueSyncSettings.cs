namespace TimecodeBridge.Core.Models;

/// <summary>
/// Cue-Syncワンショット送信の設定。プロジェクト永続化の単位。
/// </summary>
public class CueSyncSettings
{
    /// <summary>送信先OSCアドレス。</summary>
    public string OscAddress { get; set; } = "/cuesync";

    /// <summary>送信先ホストID。</summary>
    public List<string> TargetHostIds { get; set; } = [];
}
