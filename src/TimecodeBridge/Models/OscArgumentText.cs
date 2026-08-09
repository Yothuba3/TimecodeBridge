using System.Globalization;
using System.Text;

namespace TimecodeBridge.Models;

/// <summary>
/// OSC引数を「型:値」をスペース区切りで並べたテキスト記法（例: <c>i:1 f:0.5 s:hello</c>）と
/// <see cref="OscArgument"/> のリストとの間で相互変換するヘルパ。
/// 空白や引用符を含む文字列は <c>s:"hello world"</c> のように引用符で囲む（\" と \\ でエスケープ）。
/// キュー編集とOSCポン出しボタン編集の双方で共用し、記法の挙動を統一する。
/// </summary>
public static class OscArgumentText
{
    /// <summary>引数リストをテキスト記法に整形する。空のときは空文字列を返す。</summary>
    public static string Format(IReadOnlyList<OscArgument> args)
    {
        if (args.Count == 0) return string.Empty;
        // 小数点記号がカンマのロケールでも往復できるよう常にインバリアント表記
        return string.Join(" ", args.Select(a => a switch
        {
            OscInt32Argument i => $"i:{i.Value.ToString(CultureInfo.InvariantCulture)}",
            OscFloat32Argument f => $"f:{f.Value.ToString(CultureInfo.InvariantCulture)}",
            OscStringArgument s => $"s:{QuoteIfNeeded(s.Value)}",
            _ => string.Empty,
        }));
    }

    // 空白・引用符・バックスラッシュを含む値と空文字列は引用符で囲み、往復時の欠落を防ぐ
    private static string QuoteIfNeeded(string value)
    {
        if (value.Length > 0 && !value.Any(char.IsWhiteSpace) && !value.Contains('"') && !value.Contains('\\'))
            return value;
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    /// <summary>テキスト記法を引数リストへパースする。不正なトークンは無視する。</summary>
    public static List<OscArgument> Parse(string text)
    {
        var result = new List<OscArgument>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        int i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }

            // トークン内の「型:」プレフィックスを特定する（トークン外のコロンは対象外）
            int tokenEnd = i;
            while (tokenEnd < text.Length && !char.IsWhiteSpace(text[tokenEnd])) tokenEnd++;
            int colon = text.IndexOf(':', i);
            if (colon < 0 || colon >= tokenEnd) { i = tokenEnd; continue; }

            var typePrefix = text[i..colon];
            i = colon + 1;

            string valueStr;
            bool quoted = i < text.Length && text[i] == '"';
            if (quoted)
            {
                // 引用符付きの値は空白をまたいで閉じ引用符まで読む
                var sb = new StringBuilder();
                i++;
                while (i < text.Length)
                {
                    char c = text[i];
                    if (c == '\\' && i + 1 < text.Length) { sb.Append(text[i + 1]); i += 2; continue; }
                    if (c == '"') { i++; break; }
                    sb.Append(c);
                    i++;
                }
                valueStr = sb.ToString();
            }
            else
            {
                int end = i;
                while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
                valueStr = text[i..end];
                i = end;
            }

            switch (typePrefix)
            {
                case "i" when int.TryParse(valueStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv):
                    result.Add(new OscInt32Argument(iv));
                    break;
                case "f" when float.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv):
                    result.Add(new OscFloat32Argument(fv));
                    break;
                case "s" when quoted || valueStr.Length > 0:
                    result.Add(new OscStringArgument(valueStr));
                    break;
            }
        }

        return result;
    }
}
