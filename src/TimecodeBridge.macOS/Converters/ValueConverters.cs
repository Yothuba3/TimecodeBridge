using Avalonia.Data.Converters;

namespace TimecodeBridge.macOS.Converters;

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
}
