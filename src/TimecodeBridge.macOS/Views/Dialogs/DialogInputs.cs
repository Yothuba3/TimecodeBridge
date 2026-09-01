using TimecodeBridge.Core.Models;
using TimecodeBridge.macOS.ViewModels;

namespace TimecodeBridge.macOS.Views.Dialogs;

/// <summary>
/// ダイアログ間で共通の入力文字列とモデルの相互変換
/// </summary>
internal static class DialogInputs
{
    public static string FormatArguments(List<OscArgument> args)
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

    public static List<OscArgument> ParseArguments(string? text)
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

    /// <summary>
    /// 各欄の文字列からオフセットを組み立てる。全て0なら null(オフセットなし)。
    /// </summary>
    public static TimecodeOffset? ParseOffset(bool isNegative, string? hours, string? minutes, string? seconds, string? frames, FrameRate frameRate)
    {
        if (!int.TryParse(hours, out var h)) h = 0;
        if (!int.TryParse(minutes, out var m)) m = 0;
        if (!int.TryParse(seconds, out var s)) s = 0;
        if (!int.TryParse(frames, out var f)) f = 0;

        if (h == 0 && m == 0 && s == 0 && f == 0)
            return null;

        return new TimecodeOffset(isNegative, h, m, s, f, frameRate);
    }

    public static List<HostSelection> ToHostSelections(IReadOnlyList<OscHost> hosts, IReadOnlyCollection<string> selectedIds)
    {
        return hosts.Select(h => new HostSelection
        {
            Id = h.Id,
            Name = $"{h.Name} ({h.IpAddress}:{h.Port})",
            IsSelected = selectedIds.Contains(h.Id),
        }).ToList();
    }
}
