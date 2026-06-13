using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TimecodeBridge.Models;

namespace TimecodeBridge.ViewModels;

/// <summary>
/// OSCポン出しグリッドの1セル分の表示状態。設定済みボタンの有無・表示テキスト・
/// 送出時の一時ハイライトを保持する。
/// </summary>
public partial class OscTriggerCellViewModel : ObservableObject
{
    private DispatcherTimer? _flashTimer;

    public int Row { get; }
    public int Column { get; }

    [ObservableProperty] private bool _isConfigured;
    [ObservableProperty] private string _displayText = string.Empty;
    [ObservableProperty] private bool _isFlashing;

    /// <summary>このセルに紐づくボタン設定。未設定セルでは null。</summary>
    public OscTriggerButton? Button { get; private set; }

    public OscTriggerCellViewModel(int row, int column)
    {
        Row = row;
        Column = column;
    }

    /// <summary>セルにボタン設定を反映する（null で未設定化）。</summary>
    public void SetButton(OscTriggerButton? button)
    {
        Button = button;
        IsConfigured = button is not null;
        DisplayText = button is null
            ? string.Empty
            : (string.IsNullOrWhiteSpace(button.Label) ? button.OscAddress : button.Label);
    }

    /// <summary>送出時の一時的な視覚フィードバックを発生させる。</summary>
    public void Flash()
    {
        IsFlashing = true;
        _flashTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _flashTimer.Stop();
        _flashTimer.Tick -= OnFlashTick;
        _flashTimer.Tick += OnFlashTick;
        _flashTimer.Start();
    }

    private void OnFlashTick(object? sender, EventArgs e)
    {
        IsFlashing = false;
        _flashTimer?.Stop();
    }
}
