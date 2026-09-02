using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TimecodeBridge.App.Views;

namespace TimecodeBridge.App.Tests.Views;

public class StartupErrorWindowTests
{
    [AvaloniaFact]
    public void Create_例外の内容とログの保存先を表示する()
    {
        var window = StartupErrorWindow.Create(new InvalidOperationException("boom-init"), "/tmp/startup-error.log");

        var detail = window.GetLogicalDescendants().OfType<TextBox>().Single();
        var blocks = window.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();

        Assert.Contains("boom-init", detail.Text);
        Assert.True(detail.IsReadOnly);
        Assert.Contains(blocks, t => t.Contains("/tmp/startup-error.log"));
    }

    [AvaloniaFact]
    public void Create_ログが書けなかった場合はその旨を表示する()
    {
        var window = StartupErrorWindow.Create(new Exception("x"), logPath: null);

        var blocks = window.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();

        Assert.Contains(blocks, t => t.Contains("保存できませんでした"));
    }
}
