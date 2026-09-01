using Avalonia.Data.Converters;

namespace TimecodeBridge.App.Converters;

public static class ValueConverters
{
    /// <summary>継続送信トグルのラベル (true→ON / false→OFF)</summary>
    public static readonly IValueConverter BoolToToggleLabel =
        new FuncValueConverter<bool, string>(v => v ? "ON" : "OFF");

    /// <summary>送信間隔コンボの Custom(index=1) 選択時のみ有効</summary>
    public static readonly IValueConverter IndexIsCustom =
        new FuncValueConverter<int, bool>(index => index == 1);

    /// <summary>コレクション件数が0のとき true</summary>
    public static readonly IValueConverter IsZero =
        new FuncValueConverter<int, bool>(count => count == 0);

    /// <summary>未設定セルは「＋」を表示する</summary>
    public static readonly IValueConverter CellLabel =
        new FuncValueConverter<string?, string>(text => string.IsNullOrEmpty(text) ? "＋" : text);

    /// <summary>ポン出し実行モード時の警告枠</summary>
    public static readonly IValueConverter PlayModeBorderBrush =
        new FuncValueConverter<bool, Avalonia.Media.IBrush>(play =>
            play ? Avalonia.Media.Brush.Parse("#EF5A6F") : Avalonia.Media.Brush.Parse("#222240"));

    public static readonly IValueConverter PlayModeBorderThickness =
        new FuncValueConverter<bool, Avalonia.Thickness>(play =>
            play ? new Avalonia.Thickness(2) : new Avalonia.Thickness(1));
}

/// <summary>
/// StatusText文字列に応じた状態色（WPF版のStatusIndicatorStyle/ReceiveStatusTextStyle相当）
/// </summary>
public static class StatusConverters
{
    private static readonly Avalonia.Media.IBrush Idle = Avalonia.Media.Brush.Parse("#4A4A68");
    private static readonly Avalonia.Media.IBrush Success = Avalonia.Media.Brush.Parse("#4ADE80");
    private static readonly Avalonia.Media.IBrush Warning = Avalonia.Media.Brush.Parse("#F5B942");
    private static readonly Avalonia.Media.IBrush Error = Avalonia.Media.Brush.Parse("#EF5A6F");
    private static readonly Avalonia.Media.IBrush SecondaryText = Avalonia.Media.Brush.Parse("#8888A8");
    private static readonly Avalonia.Media.IBrush SubtleBorder = Avalonia.Media.Brush.Parse("#222240");

    public static readonly IValueConverter ToDotBrush =
        new FuncValueConverter<string?, Avalonia.Media.IBrush>(status => status switch
        {
            "受信中" or "生成中" => Success,
            "フリーラン" => Warning,
            "信号喪失" or "エラー" => Error,
            _ => Idle,
        });

    public static readonly IValueConverter ToTextBrush =
        new FuncValueConverter<string?, Avalonia.Media.IBrush>(status => status switch
        {
            "受信中" or "生成中" => Success,
            "フリーラン" => Warning,
            "信号喪失" or "エラー" => Error,
            _ => SecondaryText,
        });

    /// <summary>信号喪失時にタイムコード表示全体の枠を警告色にする</summary>
    public static readonly IValueConverter ToPanelBorderBrush =
        new FuncValueConverter<string?, Avalonia.Media.IBrush>(status =>
            status == "信号喪失" ? Error : SubtleBorder);

    public static readonly IValueConverter ToPanelBorderThickness =
        new FuncValueConverter<string?, Avalonia.Thickness>(status =>
            status == "信号喪失" ? new Avalonia.Thickness(2) : new Avalonia.Thickness(1));
}