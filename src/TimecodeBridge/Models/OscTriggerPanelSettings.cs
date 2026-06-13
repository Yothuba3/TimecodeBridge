namespace TimecodeBridge.Models;

/// <summary>
/// OSCポン出しパネルの設定。固定グリッドの寸法と配置済みボタン集合を保持する。
/// プロジェクト永続化の単位。
/// </summary>
public class OscTriggerPanelSettings
{
    /// <summary>グリッドの行数（最小1、既定4）。</summary>
    public int Rows { get; set; } = 4;

    /// <summary>グリッドの列数（最小1、既定4）。</summary>
    public int Columns { get; set; } = 4;

    /// <summary>配置済みのトリガーボタン集合。</summary>
    public List<OscTriggerButton> Buttons { get; set; } = [];
}
