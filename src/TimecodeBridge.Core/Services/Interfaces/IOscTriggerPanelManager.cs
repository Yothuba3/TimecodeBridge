using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Core.Services.Interfaces;

/// <summary>
/// OSCポン出しパネルのグリッド構成・ボタン集合の状態を保持し、
/// ボタン押下による即時送出と永続化入出力を担うサービス。
/// </summary>
public interface IOscTriggerPanelManager
{
    int Rows { get; }
    int Columns { get; }
    IReadOnlyList<OscTriggerButton> Buttons { get; }

    /// <summary>グリッド寸法を設定する。1未満は1に補正される。縮小で範囲外になるボタンは削除される。</summary>
    void SetGridSize(int rows, int columns);

    /// <summary>指定セルのボタンを取得する。なければ null。</summary>
    OscTriggerButton? GetButtonAt(int row, int column);

    /// <summary>指定寸法へ縮小した場合に範囲外となるボタンを列挙する（状態は変更しない）。</summary>
    IReadOnlyList<OscTriggerButton> GetOutOfRangeButtons(int rows, int columns);

    /// <summary>ボタンを追加または更新する（ID一致で更新）。同一セルの他ボタンは除去され単一性を保つ。</summary>
    void UpsertButton(OscTriggerButton button);

    /// <summary>指定IDのボタンを削除する。</summary>
    void RemoveButton(string buttonId);

    /// <summary>指定ボタンのOSCメッセージを即時送出する。送出可否の結果を返す。</summary>
    TriggerResult Trigger(string buttonId);

    /// <summary>現在の状態を永続化用設定として取得する。</summary>
    OscTriggerPanelSettings GetSettings();

    /// <summary>永続化設定から状態を復元する。</summary>
    void LoadSettings(OscTriggerPanelSettings settings);

    /// <summary>状態を既定（空グリッド・既定寸法）に初期化する。</summary>
    void Clear();

    /// <summary>グリッド寸法またはボタン集合が変化したときに発火する。</summary>
    event EventHandler? Changed;
}

/// <summary>ボタン送出の結果。</summary>
public readonly record struct TriggerResult(bool Sent, TriggerSkipReason Reason);

/// <summary>送出がスキップされた理由。</summary>
public enum TriggerSkipReason
{
    /// <summary>送出された（スキップなし）。</summary>
    None,

    /// <summary>ボタンが未設定（存在しない、またはOSCアドレス未割り当て）。</summary>
    NotConfigured,

    /// <summary>送信先が未設定、または有効な送信先が無い。</summary>
    NoEnabledTarget,
}
