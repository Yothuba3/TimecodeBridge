using TimecodeBridge.Models;

namespace TimecodeBridge.Services.Interfaces;

/// <summary>
/// OSCポン出しボタンの編集ダイアログ表示を担当するサービス。
/// </summary>
public interface IOscTriggerDialogService
{
    /// <summary>
    /// ボタン編集ダイアログを表示する。OKで確定した場合は編集後のボタンを、
    /// キャンセル時は null を返す。
    /// </summary>
    OscTriggerButton? ShowEditDialog(OscTriggerButton template, IReadOnlyList<OscHost> hosts, string title);
}
