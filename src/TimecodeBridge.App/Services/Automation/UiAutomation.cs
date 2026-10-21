using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace TimecodeBridge.App.Services.Automation;

/// <summary>
/// 自動操作で対象コントロールを指す条件。全部省略すると最初の可視要素。
/// Text は TextBlock/Button/CheckBox/MenuItem/TextBox などの表示文字列(trim 後)との完全一致、TextContains は部分一致。
/// </summary>
public sealed record UiSelector(
    string? Window = null,
    string? Id = null,
    string? Name = null,
    string? Text = null,
    string? TextContains = null,
    string? Type = null,
    string? Class = null,
    int Index = 0,
    bool IncludeHidden = false)
{
    public static UiSelector Parse(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) throw new JsonException("セレクタは JSON オブジェクトで指定してください");
        return new UiSelector(
            Window: Str(e, "window"),
            Id: Str(e, "id"),
            Name: Str(e, "name"),
            Text: Str(e, "text"),
            TextContains: Str(e, "textContains"),
            Type: Str(e, "type"),
            Class: Str(e, "class"),
            Index: e.TryGetProperty("index", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32() : 0,
            IncludeHidden: e.TryGetProperty("includeHidden", out var h) && h.ValueKind == JsonValueKind.True);
    }

    private static string? Str(JsonElement e, string key)
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public bool IsEmpty => Id is null && Name is null && Text is null && TextContains is null && Type is null && Class is null;

    public override string ToString()
        => string.Join(" ", new[]
        {
            Id is null ? null : $"id={Id}",
            Name is null ? null : $"name={Name}",
            Text is null ? null : $"text={Text}",
            TextContains is null ? null : $"textContains={TextContains}",
            Type is null ? null : $"type={Type}",
            Class is null ? null : $"class={Class}",
            Index == 0 ? null : $"index={Index}",
        }.Where(x => x is not null));
}

/// <summary>ビジュアルツリーの 1 要素。Id はウィンドウ内の位置("0/2/1" のような子インデックスの連なり)。</summary>
public sealed record UiNode(
    string Id,
    string Type,
    string? Name,
    string? Text,
    bool Visible,
    bool Enabled,
    bool Focused,
    double X,
    double Y,
    double W,
    double H,
    string? Classes,
    bool? Checked,
    int? SelectedIndex,
    double? Value);

/// <summary>
/// Avalonia のビジュアルツリーを外から読む・操作するための UI スレッド専用ヘルパー。
/// ボタンは OnClick をそのまま呼ぶ(ポインタ合成より確実で、Command と Click の両方が通常どおり動く)。
/// </summary>
public static class UiAutomation
{
    private static readonly MethodInfo ButtonOnClick =
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic, System.Type.EmptyTypes)
        ?? throw new MissingMethodException("Button.OnClick");

    private static readonly MethodInfo MenuItemOnClick =
        typeof(MenuItem).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(RoutedEventArgs)])
        ?? throw new MissingMethodException("MenuItem.OnClick");

    /// <summary>
    /// ウィンドウ指定の解決。null/空は「最後に開いた可視ウィンドウ」(モーダルが出ていればそれ)、
    /// "main" は最初のウィンドウ、整数は一覧のインデックス、それ以外はタイトルの部分一致。
    /// </summary>
    public static Window ResolveWindow(IReadOnlyList<Window> windows, string? spec)
    {
        if (windows.Count == 0) throw new InvalidOperationException("開いているウィンドウがありません");
        if (string.IsNullOrWhiteSpace(spec))
            return windows.LastOrDefault(w => w.IsVisible) ?? windows[^1];
        if (spec.Equals("main", StringComparison.OrdinalIgnoreCase)) return windows[0];
        if (int.TryParse(spec, out var index))
        {
            if (index < 0 || index >= windows.Count) throw new ArgumentOutOfRangeException(nameof(spec), $"ウィンドウ {index} はありません(0..{windows.Count - 1})");
            return windows[index];
        }
        return windows.FirstOrDefault(w => (w.Title ?? "").Contains(spec, StringComparison.OrdinalIgnoreCase))
               ?? throw new KeyNotFoundException($"タイトルに「{spec}」を含むウィンドウがありません");
    }

    public static object DescribeWindow(Window w, int index) => new
    {
        index,
        type = w.GetType().Name,
        title = w.Title,
        isVisible = w.IsVisible,
        isActive = w.IsActive,
        isDialog = w.Owner is not null,
        width = w.ClientSize.Width,
        height = w.ClientSize.Height,
    };

    /// <summary>深さ優先でコントロールを列挙する。</summary>
    public static IEnumerable<(Control Control, string Id)> Walk(Visual root)
    {
        var stack = new Stack<(Visual, string)>();
        stack.Push((root, "0"));
        while (stack.Count > 0)
        {
            var (visual, id) = stack.Pop();
            if (visual is Control control) yield return (control, id);
            var children = visual.GetVisualChildren().ToList();
            for (var i = children.Count - 1; i >= 0; i--) stack.Push((children[i], $"{id}/{i}"));
        }
    }

    public static IReadOnlyList<UiNode> Tree(Window window, bool includeHidden)
        => Walk(window)
            .Where(x => includeHidden || x.Control.IsEffectivelyVisible)
            .Select(x => Describe(x.Control, x.Id, window))
            .ToList();

    public static UiNode Describe(Control c, string id, Visual root)
    {
        var origin = c.TranslatePoint(new Point(0, 0), root) ?? new Point(double.NaN, double.NaN);
        return new UiNode(
            id,
            c.GetType().Name,
            string.IsNullOrEmpty(c.Name) ? null : c.Name,
            TextOf(c),
            c.IsEffectivelyVisible,
            c.IsEffectivelyEnabled,
            c.IsFocused,
            Math.Round(origin.X, 1),
            Math.Round(origin.Y, 1),
            Math.Round(c.Bounds.Width, 1),
            Math.Round(c.Bounds.Height, 1),
            c.Classes.Count == 0 ? null : string.Join(" ", c.Classes),
            c is ToggleButton t ? t.IsChecked : null,
            c is SelectingItemsControl s ? s.SelectedIndex : null,
            c is RangeBase r ? Math.Round(r.Value, 3) : null);
    }

    /// <summary>人が見ている文字列。TextBlock の本文、ボタンや項目の Content、メニューの Header、入力欄の内容、ウィンドウのタイトル。</summary>
    public static string? TextOf(Control c)
    {
        var text = c switch
        {
            TextBlock tb => tb.Text,
            TextBox box => box.Text,
            Window w => w.Title,
            HeaderedContentControl h => h.Header as string ?? h.Content as string,
            MenuItem m => m.Header as string,
            ContentControl cc => cc.Content as string,
            SelectingItemsControl s => s.SelectedItem?.ToString(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    public static IReadOnlyList<(Control Control, string Id)> FindAll(Window window, UiSelector s)
        => Walk(window)
            .Where(x => s.IncludeHidden || x.Control.IsEffectivelyVisible)
            .Where(x => (s.Id is null || x.Id == s.Id) && Matches(x.Control, s))
            .ToList();

    public static (Control Control, string Id) Find(Window window, UiSelector s)
    {
        var all = FindAll(window, s);
        if (all.Count == 0) throw new KeyNotFoundException($"一致する要素がありません: {s}(window={window.Title})");
        if (s.Index < 0 || s.Index >= all.Count) throw new KeyNotFoundException($"一致は {all.Count} 件、index={s.Index} はありません: {s}");
        return all[s.Index];
    }

    private static bool Matches(Control c, UiSelector s)
    {
        if (s.Name is not null && !string.Equals(c.Name, s.Name, StringComparison.Ordinal)) return false;
        if (s.Type is not null && !string.Equals(c.GetType().Name, s.Type, StringComparison.Ordinal)) return false;
        if (s.Class is not null && !c.Classes.Contains(s.Class)) return false;
        if (s.Text is not null && !string.Equals(TextOf(c), s.Text.Trim(), StringComparison.Ordinal)) return false;
        if (s.TextContains is not null && !(TextOf(c)?.Contains(s.TextContains, StringComparison.Ordinal) ?? false)) return false;
        return true;
    }

    /// <summary>自分または祖先のうち、クリックの意味を持つコントロール。</summary>
    public static Control? Clickable(Control c)
        => c.GetSelfAndVisualAncestors().OfType<Control>()
            .FirstOrDefault(a => a is Button or MenuItem or ListBoxItem or TabItem or ComboBoxItem or Expander);

    /// <summary>クリック相当の操作をして、何をしたかを返す。</summary>
    public static string Click(Control matched)
    {
        var target = Clickable(matched)
            ?? throw new InvalidOperationException($"{matched.GetType().Name} とその祖先にクリックできる要素がありません");
        if (!target.IsEffectivelyEnabled) throw new InvalidOperationException($"{Label(target)} は無効(IsEnabled=false)です");

        switch (target)
        {
            case Button b:
                ButtonOnClick.Invoke(b, null);
                return $"click {Label(b)}";
            case MenuItem m:
                MenuItemOnClick.Invoke(m, [new RoutedEventArgs(MenuItem.ClickEvent)]);
                return $"click {Label(m)}";
            case ComboBoxItem cbi:
                cbi.IsSelected = true;
                return $"select {Label(cbi)}";
            case ListBoxItem item:
                return SelectItem(item, additive: false);
            case TabItem tab:
                tab.IsSelected = true;
                return $"select {Label(tab)}";
            case Expander ex:
                ex.IsExpanded = !ex.IsExpanded;
                return $"toggle {Label(ex)}";
            default:
                throw new InvalidOperationException($"{Label(target)} はクリックに対応していません");
        }
    }

    /// <summary>
    /// リスト項目を選ぶ。コンテナの IsSelected ではなく親の ListBox の選択モデルを通すので、
    /// SelectedItems にバインドしたコマンド(一括編集・削除)からも選択が見える。additive で既存の選択に追加する(Cmd+クリック相当)。
    /// </summary>
    public static string SelectItem(ListBoxItem item, bool additive)
    {
        var owner = item.FindAncestorOfType<ListBox>();
        var index = owner?.IndexFromContainer(item) ?? -1;
        if (owner is null || index < 0)
        {
            item.IsSelected = true;
            return $"select {Label(item)} (owner なし)";
        }
        if (additive) owner.Selection.Select(index);
        else owner.SelectedIndex = index;
        item.Focus();
        return $"select {Label(owner)}[{index}]{(additive ? " +" : "")}";
    }

    /// <summary>入力欄に文字列を入れる。commit で他へフォーカスを移し、LostFocus で確定するバインディングも更新させる。</summary>
    public static string Type(Control matched, string text, bool commit)
    {
        var box = matched as TextBox
                  ?? matched.GetSelfAndVisualAncestors().OfType<TextBox>().FirstOrDefault()
                  ?? matched.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()
                  ?? throw new InvalidOperationException($"{Label(matched)} は入力欄ではありません");
        if (!box.IsEffectivelyEnabled || box.IsReadOnly) throw new InvalidOperationException($"{Label(box)} は入力できません");
        box.Focus();
        box.Text = text;
        box.CaretIndex = text.Length;
        if (commit) Commit(box);
        return $"type {Label(box)} = \"{text}\"";
    }

    private static void Commit(Control c)
    {
        var top = TopLevel.GetTopLevel(c);
        top?.FocusManager?.ClearFocus();
        if (c.IsFocused) c.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
    }

    /// <summary>チェック状態・選択・数値など、クリックでは表しにくい値を直接入れる。</summary>
    public static string Set(Control matched, JsonElement value)
    {
        if (value.TryGetProperty("checked", out var ch) && ch.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            var toggle = matched as ToggleButton ?? matched.GetSelfAndVisualAncestors().OfType<ToggleButton>().FirstOrDefault()
                         ?? throw new InvalidOperationException($"{Label(matched)} はチェックできません");
            var want = ch.ValueKind == JsonValueKind.True;
            if (toggle.IsChecked != want) ButtonOnClick.Invoke(toggle, null);
            if (toggle.IsChecked != want) toggle.IsChecked = want;
            return $"set {Label(toggle)} checked={want}";
        }
        if (value.TryGetProperty("selectedIndex", out var si) && si.ValueKind == JsonValueKind.Number)
        {
            var sel = matched as SelectingItemsControl ?? matched.GetSelfAndVisualAncestors().OfType<SelectingItemsControl>().FirstOrDefault()
                      ?? throw new InvalidOperationException($"{Label(matched)} は選択できません");
            sel.SelectedIndex = si.GetInt32();
            return $"set {Label(sel)} selectedIndex={sel.SelectedIndex}";
        }
        if (value.TryGetProperty("selectedText", out var st) && st.ValueKind == JsonValueKind.String)
        {
            var sel = matched as SelectingItemsControl ?? matched.GetSelfAndVisualAncestors().OfType<SelectingItemsControl>().FirstOrDefault()
                      ?? throw new InvalidOperationException($"{Label(matched)} は選択できません");
            var needle = st.GetString()!;
            var items = (sel.ItemsSource ?? sel.Items).Cast<object?>().ToList();
            var index = items.FindIndex(x => ItemText(x).Contains(needle, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new KeyNotFoundException($"{Label(sel)} に「{needle}」を含む項目がありません: {string.Join(" | ", items.Select(ItemText))}");
            sel.SelectedIndex = index;
            return $"set {Label(sel)} selectedIndex={index} ({ItemText(items[index])})";
        }
        if (value.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number)
        {
            var range = matched as RangeBase ?? matched.GetSelfAndVisualAncestors().OfType<RangeBase>().FirstOrDefault()
                        ?? throw new InvalidOperationException($"{Label(matched)} は数値を持ちません");
            range.Value = v.GetDouble();
            return $"set {Label(range)} value={range.Value}";
        }
        if (value.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
            return Type(matched, t.GetString()!, true);
        throw new ArgumentException("checked / selectedIndex / selectedText / value / text のいずれかを指定してください");
    }

    /// <summary>
    /// キーを押して離す。フォーカス中の要素(無ければウィンドウ)に KeyDown を流し、
    /// 誰も処理しなければウィンドウの KeyBindings(HotKey もここに登録される)を自分で評価する。
    /// </summary>
    public static string SendKey(Window window, Key key, KeyModifiers modifiers)
    {
        var target = window.FocusManager?.GetFocusedElement() as Control ?? window;
        var down = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = target };
        target.RaiseEvent(down);
        var handledBy = down.Handled ? "handler" : null;
        if (!down.Handled)
        {
            foreach (var binding in window.KeyBindings)
            {
                if (!binding.Gesture.Matches(down)) continue;
                if (binding.Command?.CanExecute(binding.CommandParameter) != true) continue;
                binding.Command.Execute(binding.CommandParameter);
                handledBy = "keybinding";
                break;
            }
        }
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, KeyModifiers = modifiers, Source = target });
        return $"key {modifiers}+{key} → {handledBy ?? "unhandled"} (focus={Label(target)})";
    }

    public static KeyModifiers ParseModifiers(string? text)
    {
        var result = KeyModifiers.None;
        if (string.IsNullOrWhiteSpace(text)) return result;
        foreach (var part in text.Split(['+', ',', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            result |= part.ToLowerInvariant() switch
            {
                "cmd" or "meta" or "command" => KeyModifiers.Meta,
                "ctrl" or "control" => KeyModifiers.Control,
                "alt" or "option" => KeyModifiers.Alt,
                "shift" => KeyModifiers.Shift,
                _ => throw new ArgumentException($"不明な修飾キー: {part}"),
            };
        }
        return result;
    }

    /// <summary>ウィンドウをアプリ内でレンダリングして PNG にする(OS の画面収録権限は要らない)。</summary>
    public static byte[] Capture(Window window)
    {
        var scale = window.RenderScaling;
        var size = window.ClientSize;
        var pixels = new PixelSize(Math.Max(1, (int)Math.Round(size.Width * scale)), Math.Max(1, (int)Math.Round(size.Height * scale)));
        using var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        bitmap.Render(window);
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }

    public static string Label(Control c)
    {
        var text = TextOf(c);
        return c.GetType().Name + (c.Name is { Length: > 0 } n ? $"#{n}" : "") + (text is null ? "" : $"「{text}」");
    }

    private static string ItemText(object? item) => item switch
    {
        null => "",
        Control c => TextOf(c) ?? c.GetType().Name,
        _ => item.ToString() ?? "",
    };
}
