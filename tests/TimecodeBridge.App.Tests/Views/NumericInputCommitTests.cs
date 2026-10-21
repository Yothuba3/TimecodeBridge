using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TimecodeBridge.App.Views;

namespace TimecodeBridge.App.Tests.Views;

/// <summary>
/// 判定幅・ポン出しの行列など、LostFocus で確定する数値欄の入力検証。
/// 数値として読めない入力は「最小値へ落とす」のではなく「直前の値を保つ」。
/// 判定幅が黙って 0 になるとジャンプ判定が無効化され、頭出ししても途中のキューが発火してしまう。
/// </summary>
public class NumericInputCommitTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("3.5")]
    [InlineData("１０")] // 全角
    public void 判定幅_数値として読めない入力は直前の値を保つ(string input)
    {
        Assert.Equal(5, CueListView.CommitTriggerWindowFrames(input, current: 5));
    }

    [Theory]
    [InlineData("-1", 0)]
    [InlineData("-100", 0)]
    public void 判定幅_負数は0へ補正する(string input, int expected)
    {
        Assert.Equal(expected, CueListView.CommitTriggerWindowFrames(input, current: 5));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("3", 3)]
    [InlineData("10", 10)]
    [InlineData(" 7 ", 7)]
    public void 判定幅_正しい入力はその値になる(string input, int expected)
    {
        Assert.Equal(expected, CueListView.CommitTriggerWindowFrames(input, current: 5));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-3")]
    public void ポン出しの行列_読めない入力と1未満は直前の値を保つ(string input)
    {
        Assert.Equal(4, OscTriggerPanelView.CommitGridSize(input, current: 4));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("8", 8)]
    public void ポン出しの行列_正しい入力はその値になる(string input, int expected)
    {
        Assert.Equal(expected, OscTriggerPanelView.CommitGridSize(input, current: 4));
    }

    [AvaloniaFact]
    public void 判定幅_不正入力のあと欄の表示は実値へ戻る()
    {
        var box = new TextBox { Text = "3" };
        var window = new Window { Content = box };
        window.Show();

        box.Text = "abc";
        var committed = CueListView.CommitTriggerWindowFrames(box.Text, current: 3);
        box.Text = committed.ToString();

        Assert.Equal(3, committed);
        Assert.Equal("3", box.Text);
    }
}
