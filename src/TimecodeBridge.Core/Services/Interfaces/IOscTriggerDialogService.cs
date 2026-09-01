using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Core.Services.Interfaces;

/// <summary>
/// OSCポン出しボタンの編集ダイアログ表示を担当するサービス。
/// </summary>
public interface IOscTriggerDialogService
{
    /// <summary>
    /// ボタン編集ダイアログを表示する。
    /// </summary>
    /// <param name="template">編集対象（新規時は空のボタン）。</param>
    /// <param name="hosts">選択可能なホスト一覧。</param>
    /// <param name="title">ダイアログタイトル。</param>
    /// <param name="canDelete">削除操作を許可するか（既存ボタン編集時に true）。</param>
    OscTriggerEditResult ShowEditDialog(OscTriggerButton template, IReadOnlyList<OscHost> hosts, string title, bool canDelete);
}

/// <summary>編集ダイアログで選択された操作。</summary>
public enum OscTriggerEditAction
{
    /// <summary>キャンセル（変更なし）。</summary>
    Cancel,

    /// <summary>保存（<see cref="OscTriggerEditResult.Button"/> に編集結果）。</summary>
    Save,

    /// <summary>削除。</summary>
    Delete,
}

/// <summary>編集ダイアログの結果。</summary>
public readonly record struct OscTriggerEditResult(OscTriggerEditAction Action, OscTriggerButton? Button);
