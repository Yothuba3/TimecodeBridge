namespace TimecodeBridge.Models;

/// <summary>
/// OSCポン出しパネルのグリッド上に配置される1つのトリガーボタン。
/// 押下時に設定されたOSCアドレス・引数を送信先ホストへ即時送出する。
/// </summary>
public class OscTriggerButton
{
    /// <summary>ボタンを一意に識別するID（GUID文字列）。</summary>
    public required string Id { get; set; }

    /// <summary>グリッド上の行位置（0始まり）。</summary>
    public int Row { get; set; }

    /// <summary>グリッド上の列位置（0始まり）。</summary>
    public int Column { get; set; }

    /// <summary>ボタンに表示するラベル。</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>送出するOSCアドレス。</summary>
    public string OscAddress { get; set; } = string.Empty;

    /// <summary>送出するOSC引数（順序を保持）。</summary>
    public List<OscArgument> Arguments { get; set; } = [];

    /// <summary>送信先ホストID。</summary>
    public List<string> TargetHostIds { get; set; } = [];
}
