using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Core.Services.Interfaces;

/// <summary>
/// OSCホスト編集ダイアログサービス
/// </summary>
public interface IHostDialogService
{
    /// <summary>
    /// ホスト編集ダイアログを表示する
    /// </summary>
    OscHost? ShowEditDialog(OscHost template);
}
