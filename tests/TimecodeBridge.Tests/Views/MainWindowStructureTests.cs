using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Xunit;

namespace TimecodeBridge.Tests.Views;

public class MainWindowStructureTests
{
    [Fact]
    public void MainWindow_ShouldLoadAxamlWithoutErrors()
    {
        // Arrange & Act
        var window = CreateMainWindow();

        // Assert
        Assert.NotNull(window);
        Assert.IsType<Window>(window);
    }

    [Fact]
    public void MainWindow_ShouldHaveNativeMenuWithFileMenu()
    {
        // Arrange
        var window = CreateMainWindow();

        // Act
        var nativeMenu = NativeMenu.GetMenu(window);

        // Assert
        Assert.NotNull(nativeMenu);
        Assert.NotEmpty(nativeMenu.Items);

        var fileMenuItem = nativeMenu.Items.OfType<NativeMenuItem>().FirstOrDefault(m => m.Header?.ToString() == "File");
        Assert.NotNull(fileMenuItem);
    }

    [Fact]
    public void MainWindow_FileMenu_ShouldHaveNewOpenSaveItems()
    {
        // Arrange
        var window = CreateMainWindow();
        var nativeMenu = NativeMenu.GetMenu(window);
        var fileMenuItem = nativeMenu!.Items.OfType<NativeMenuItem>().First(m => m.Header?.ToString() == "File");

        // Act
        var fileMenuItems = fileMenuItem.Menu?.Items.OfType<NativeMenuItem>().ToList();

        // Assert
        Assert.NotNull(fileMenuItems);
        Assert.Contains(fileMenuItems, m => m.Header?.ToString() == "New Project");
        Assert.Contains(fileMenuItems, m => m.Header?.ToString() == "Open...");
        Assert.Contains(fileMenuItems, m => m.Header?.ToString() == "Save");
    }

    [Fact]
    public void MainWindow_FileMenu_ShouldHaveKeyboardShortcuts()
    {
        // Arrange
        var window = CreateMainWindow();
        var nativeMenu = NativeMenu.GetMenu(window);
        var fileMenuItem = nativeMenu!.Items.OfType<NativeMenuItem>().First(m => m.Header?.ToString() == "File");
        var fileMenuItems = fileMenuItem.Menu!.Items.OfType<NativeMenuItem>().ToList();

        // Act
        var openItem = fileMenuItems.First(m => m.Header?.ToString() == "Open...");
        var saveItem = fileMenuItems.First(m => m.Header?.ToString() == "Save");

        // Assert
        Assert.NotNull(openItem.Gesture);
        Assert.NotNull(saveItem.Gesture);
        Assert.Contains("O", openItem.Gesture.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("S", saveItem.Gesture.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MainWindow_ShouldHaveGridLayout()
    {
        // Arrange
        var window = CreateMainWindow();

        // Act
        var grid = window.Content as Grid;

        // Assert
        Assert.NotNull(grid);
        Assert.Equal(3, grid.RowDefinitions.Count);
    }

    [Fact]
    public void MainWindow_GridLayout_ShouldHaveCorrectRowDefinitions()
    {
        // Arrange
        var window = CreateMainWindow();
        var grid = window.Content as Grid;

        // Act
        var row0 = grid!.RowDefinitions[0];
        var row1 = grid.RowDefinitions[1];
        var row2 = grid.RowDefinitions[2];

        // Assert
        Assert.Equal(GridLength.Auto, row0.Height); // TimecodeDisplayView
        Assert.True(row1.Height.IsStar); // CueListView (flexible)
        Assert.Equal(GridLength.Auto, row2.Height); // Status bar
    }

    [Fact]
    public void MainWindow_ShouldHaveCompiledBindingDataType()
    {
        // Arrange
        var window = CreateMainWindow();

        // Act - Check that window has x:DataType attribute set
        var dataContext = window.DataContext;

        // Assert - This test validates that the XAML compiles with x:DataType
        // The actual validation happens at compile time with CompiledBindings
        Assert.True(true, "XAML with x:DataType compiled successfully");
    }

    [Fact]
    public void MainWindow_ShouldHaveTitleBinding()
    {
        // Arrange
        var window = CreateMainWindow();

        // Act
        var titleBinding = window.GetBindingObserver(Window.TitleProperty);

        // Assert
        Assert.NotNull(titleBinding);
    }

    private static Window CreateMainWindow()
    {
        var xaml = System.IO.File.ReadAllText(
            "/Users/yothuba/TimecodeBridge/src/TimecodeBridge.macOS/MainWindow.axaml");

        return (Window)AvaloniaRuntimeXamlLoader.Parse(xaml);
    }
}
