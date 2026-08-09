using System.Windows.Controls;
using System.Windows.Input;
using TimecodeBridge.ViewModels;

namespace TimecodeBridge.Views;

public partial class OscTriggerPanelView : UserControl
{
    public OscTriggerPanelView()
    {
        InitializeComponent();
        PreviewKeyDown += OnPanelPreviewKeyDown;
    }

    // Esc で実行モードから安全側（編集モード）へ即座に戻す
    private void OnPanelPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is OscTriggerPanelViewModel { IsPlayMode: true } vm)
        {
            vm.IsEditMode = true;
            e.Handled = true;
        }
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
