using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using System.Windows.Controls;
using System.Windows.Input;

namespace TimecodeBridge.Views;

public partial class OscTriggerPanelView : UserControl
{
    public OscTriggerPanelView()
    {
        InitializeComponent();
        // Esc→編集モード復帰はフォーカス位置によらず効くよう MainWindow 側で処理する
    }

    // 行/列入力で Enter を押したら即時確定する（LostFocus を待たずにグリッドへ反映）。
    private void OnSizeBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox textBox)
        {
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
    }
}
