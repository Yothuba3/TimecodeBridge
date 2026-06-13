namespace TimecodeBridge.Models;

/// <summary>
/// OSC引数を「型:値」をスペース区切りで並べたテキスト記法（例: <c>i:1 f:0.5 s:hello</c>）と
/// <see cref="OscArgument"/> のリストとの間で相互変換するヘルパ。
/// キュー編集とOSCポン出しボタン編集の双方で共用し、記法の挙動を統一する。
/// </summary>
public static class OscArgumentText
{
    /// <summary>引数リストをテキスト記法に整形する。空のときは空文字列を返す。</summary>
    public static string Format(IReadOnlyList<OscArgument> args)
    {
        if (args.Count == 0) return string.Empty;
        return string.Join(" ", args.Select(a => a switch
        {
            OscInt32Argument i => $"i:{i.Value}",
            OscFloat32Argument f => $"f:{f.Value}",
            OscStringArgument s => $"s:{s.Value}",
            _ => string.Empty,
        }));
    }

    /// <summary>テキスト記法を引数リストへパースする。不正なトークンは無視する。</summary>
    public static List<OscArgument> Parse(string text)
    {
        var result = new List<OscArgument>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var colonIndex = token.IndexOf(':');
            if (colonIndex < 1 || colonIndex >= token.Length - 1) continue;

            var typePrefix = token[..colonIndex];
            var valueStr = token[(colonIndex + 1)..];

            switch (typePrefix)
            {
                case "i" when int.TryParse(valueStr, out var iv):
                    result.Add(new OscInt32Argument(iv));
                    break;
                case "f" when float.TryParse(valueStr, out var fv):
                    result.Add(new OscFloat32Argument(fv));
                    break;
                case "s":
                    result.Add(new OscStringArgument(valueStr));
                    break;
            }
        }

        return result;
    }
}
