using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TimecodeBridge.App.Views;

namespace TimecodeBridge.App.Tests.Views;

public class HostManagerViewTests
{
    [AvaloniaFact]
    public void Constructor_InitializesSuccessfully()
    {
        // Arrange & Act
        var view = new HostManagerView();

        // Assert
        Assert.NotNull(view);
    }

    [AvaloniaFact]
    public void View_HasHostList()
    {
        // Arrange
        var view = new HostManagerView();

        // Act
        var hostList = view.FindControl<Avalonia.Controls.ListBox>("HostList");

        // Assert
        Assert.NotNull(hostList);
    }

    [AvaloniaFact]
    public void View_HasAddButton()
    {
        // Arrange
        var view = new HostManagerView();

        // Act
        var addButton = view.FindControl<Avalonia.Controls.Button>("AddHostButton");

        // Assert
        Assert.NotNull(addButton);
    }
}
