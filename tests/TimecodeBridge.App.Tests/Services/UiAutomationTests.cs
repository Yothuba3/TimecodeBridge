using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using TimecodeBridge.App.Services.Automation;

namespace TimecodeBridge.App.Tests.Services;

/// <summary>
/// 自動操作口(tcb2ctl)が使う UI ヘルパー。実機では Avalonia のウィンドウ、ここでは headless のウィンドウで同じ経路を通す。
/// </summary>
public class UiAutomationTests
{
    private static Window BuildWindow(out int clicks, out TextBox box)
    {
        clicks = 0;
        var counter = new int[1];
        var button = new Button { Content = "＋ 追加", Command = new RelayCommand(() => counter[0]++) };
        box = new TextBox { Name = "NameBox", Text = "" };
        var hidden = new Button { Content = "隠れたボタン", IsVisible = false };
        var window = new Window
        {
            Title = "テスト",
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "見出し" },
                    button,
                    box,
                    hidden,
                    new CheckBox { Name = "EnabledBox", Content = "有効", IsChecked = false },
                    new Button { Content = new TextBlock { Text = "LIVE" }, Name = "MuteToggle" },
                },
            },
        };
        window.Show();
        clicks = counter[0];
        return window;
    }

    [AvaloniaFact]
    public void Find_MatchesByTextNameAndTypeAndSkipsHiddenByDefault()
    {
        var window = BuildWindow(out _, out _);

        Assert.Equal("Button", UiAutomation.Find(window, new UiSelector(Text: "＋ 追加")).Control.GetType().Name);
        Assert.Equal("NameBox", UiAutomation.Find(window, new UiSelector(Name: "NameBox")).Control.Name);
        Assert.Equal("CheckBox", UiAutomation.Find(window, new UiSelector(Type: "CheckBox")).Control.GetType().Name);
        Assert.Throws<KeyNotFoundException>(() => UiAutomation.Find(window, new UiSelector(Text: "隠れたボタン")));
        Assert.Equal("Button", UiAutomation.Find(window, new UiSelector(Text: "隠れたボタン", IncludeHidden: true)).Control.GetType().Name);
        Assert.Throws<KeyNotFoundException>(() => UiAutomation.Find(window, new UiSelector(Text: "存在しない")));
    }

    [AvaloniaFact]
    public void Click_RunsTheButtonCommand_AndWalksUpFromInnerText()
    {
        var count = 0;
        var window = new Window
        {
            Content = new Button
            {
                Content = new TextBlock { Text = "CUE SYNC" },
                Command = new RelayCommand(() => count++),
            },
        };
        window.Show();

        var inner = UiAutomation.Find(window, new UiSelector(Text: "CUE SYNC")).Control;
        Assert.IsType<TextBlock>(inner);
        var result = UiAutomation.Click(inner);

        Assert.Equal(1, count);
        Assert.StartsWith("click Button", result);
    }

    [AvaloniaFact]
    public void Click_RefusesDisabledButton()
    {
        var window = new Window { Content = new Button { Content = "編集", IsEnabled = false } };
        window.Show();

        var ex = Assert.Throws<InvalidOperationException>(() => UiAutomation.Click(UiAutomation.Find(window, new UiSelector(Text: "編集")).Control));
        Assert.Contains("無効", ex.Message);
    }

    [AvaloniaFact]
    public void Type_SetsTextAndMovesFocusAway()
    {
        var window = BuildWindow(out _, out var box);

        UiAutomation.Type(box, "QLab 本番", commit: true);

        Assert.Equal("QLab 本番", box.Text);
        Assert.False(box.IsFocused);
    }

    [AvaloniaFact]
    public void Set_ChecksCheckBoxAndSelectsComboBoxItemByText()
    {
        var combo = new ComboBox { ItemsSource = new[] { "なし", "Pro Tools Audio Bridge 2-A", "MacBook Air" } };
        var check = new CheckBox { Content = "有効", IsChecked = false };
        var window = new Window { Content = new StackPanel { Children = { combo, check } } };
        window.Show();

        UiAutomation.Set(check, JsonDocument.Parse("""{"checked":true}""").RootElement);
        UiAutomation.Set(combo, JsonDocument.Parse("""{"selectedText":"bridge 2-a"}""").RootElement);

        Assert.True(check.IsChecked);
        Assert.Equal(1, combo.SelectedIndex);
        Assert.Throws<KeyNotFoundException>(() => UiAutomation.Set(combo, JsonDocument.Parse("""{"selectedText":"存在しない"}""").RootElement));
    }

    [AvaloniaFact]
    public void SendKey_FallsBackToWindowKeyBindings()
    {
        var saved = 0;
        var window = new Window { Content = new TextBlock { Text = "x" } };
        window.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.S, KeyModifiers.Meta), Command = new RelayCommand(() => saved++) });
        window.Show();

        var result = UiAutomation.SendKey(window, Key.S, KeyModifiers.Meta);

        Assert.Equal(1, saved);
        Assert.Contains("keybinding", result);
    }

    [AvaloniaFact]
    public void Tree_ListsVisibleControlsWithTextAndIds()
    {
        var window = BuildWindow(out _, out _);

        var nodes = UiAutomation.Tree(window, includeHidden: false);

        Assert.Contains(nodes, n => n.Type == "Button" && n.Text == "＋ 追加");
        Assert.Contains(nodes, n => n.Type == "TextBox" && n.Name == "NameBox");
        Assert.DoesNotContain(nodes, n => n.Text == "隠れたボタン");
        Assert.All(nodes, n => Assert.StartsWith("0", n.Id));
    }

    [AvaloniaFact]
    public void ResolveWindow_DefaultsToLastVisibleWindow()
    {
        var main = new Window { Title = "TimecodeBridge2" };
        var dialog = new Window { Title = "ホストを追加" };
        main.Show();
        dialog.Show();
        var windows = new[] { main, dialog };

        Assert.Same(dialog, UiAutomation.ResolveWindow(windows, null));
        Assert.Same(main, UiAutomation.ResolveWindow(windows, "main"));
        Assert.Same(dialog, UiAutomation.ResolveWindow(windows, "ホスト"));
        Assert.Same(main, UiAutomation.ResolveWindow(windows, "0"));
        Assert.Throws<KeyNotFoundException>(() => UiAutomation.ResolveWindow(windows, "無い"));
    }

    [Fact]
    public void Selector_ParsesJsonAndModifiers()
    {
        var selector = UiSelector.Parse(JsonDocument.Parse("""{"text":"編集","type":"Button","index":2,"includeHidden":true,"window":"main"}""").RootElement);

        Assert.Equal(new UiSelector(Window: "main", Text: "編集", Type: "Button", Index: 2, IncludeHidden: true), selector);
        Assert.Equal(KeyModifiers.Meta | KeyModifiers.Shift, UiAutomation.ParseModifiers("Cmd+Shift"));
        Assert.Throws<ArgumentException>(() => UiAutomation.ParseModifiers("Hyper"));
    }

    [Fact]
    public void SelfTestStart_ParsesTimecodeOrReturnsNull()
    {
        Assert.Equal("10:00:00:00", DevSelfTest.ParseStart("10:00:00:00")!.Value.ToString());
        Assert.Null(DevSelfTest.ParseStart("10:00"));
        Assert.Null(DevSelfTest.ParseStart("24:00:00:00"));
        Assert.Null(DevSelfTest.ParseStart(null));
    }
}
