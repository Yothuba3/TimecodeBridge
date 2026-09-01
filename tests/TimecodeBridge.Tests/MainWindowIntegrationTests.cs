using TimecodeBridge.macOS.ViewModels;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.Services.Interfaces;
using Xunit;
using Moq;

namespace TimecodeBridge.Tests;

/// <summary>
/// MainWindow統合テスト（Task 6対応）
/// </summary>
public class MainWindowIntegrationTests
{
    [Fact]
    public void MainViewModel_ShouldInitializeAllChildViewModels()
    {
        // Arrange
        var mockProjectService = new Mock<IProjectService>();
        var mockFileDialogService = new Mock<IFileDialogService>();
        var mockRecentProjectsService = new Mock<IRecentProjectsService>();
        var mockCueManager = new Mock<ICueManager>();
        var mockHostRegistry = new Mock<IHostRegistry>();
        var mockTimecodeRelay = new Mock<ITimecodeRelay>();
        var mockTimecodeEngine = new Mock<ITimecodeEngine>();
        var mockTimecodeViewModel = new object();
        var mockCueListViewModel = new object();
        var mockRelayViewModel = new object();
        var hostManagerViewModel = new HostManagerViewModel(mockHostRegistry.Object, Mock.Of<IFileDialogService>());
        var logViewModel = new LogViewModel(mockFileDialogService.Object);

        mockRecentProjectsService.Setup(x => x.GetRecentProjects()).Returns(new List<string>());
        mockProjectService.Setup(x => x.HasUnsavedChanges).Returns(false);

        // Act
        var viewModel = new MainViewModel(
            mockProjectService.Object,
            mockFileDialogService.Object,
            mockRecentProjectsService.Object,
            mockCueManager.Object,
            mockHostRegistry.Object,
            mockTimecodeRelay.Object,
            mockTimecodeEngine.Object,
            mockTimecodeViewModel,
            mockCueListViewModel,
            mockRelayViewModel,
            hostManagerViewModel,
            logViewModel);

        // Assert
        Assert.NotNull(viewModel.TimecodeViewModel);
        Assert.NotNull(viewModel.CueListViewModel);
        Assert.NotNull(viewModel.HostManagerViewModel);
        Assert.NotNull(viewModel.LogViewModel);
        Assert.Equal("TimecodeBridge", viewModel.Title);
        Assert.False(viewModel.HasUnsavedChanges);
    }

    [Fact]
    public void MainViewModel_ShouldUpdateStatusMessage_WhenInitialized()
    {
        // Arrange
        var mockProjectService = new Mock<IProjectService>();
        var mockFileDialogService = new Mock<IFileDialogService>();
        var mockRecentProjectsService = new Mock<IRecentProjectsService>();
        var mockCueManager = new Mock<ICueManager>();
        var mockHostRegistry = new Mock<IHostRegistry>();
        var mockTimecodeRelay = new Mock<ITimecodeRelay>();
        var mockTimecodeEngine = new Mock<ITimecodeEngine>();
        var hostManagerViewModel = new HostManagerViewModel(mockHostRegistry.Object, Mock.Of<IFileDialogService>());
        var logViewModel = new LogViewModel(mockFileDialogService.Object);

        mockRecentProjectsService.Setup(x => x.GetRecentProjects()).Returns(new List<string>());
        mockProjectService.Setup(x => x.HasUnsavedChanges).Returns(false);

        // Act
        var viewModel = new MainViewModel(
            mockProjectService.Object,
            mockFileDialogService.Object,
            mockRecentProjectsService.Object,
            mockCueManager.Object,
            mockHostRegistry.Object,
            mockTimecodeRelay.Object,
            mockTimecodeEngine.Object,
            new object(),
            new object(),
            new object(),
            hostManagerViewModel,
            logViewModel);

        // Assert
        Assert.Contains("アプリケーションを起動しました", viewModel.StatusMessage);
        Assert.Single(logViewModel.LogEntries);
        Assert.Contains("アプリケーションを起動しました", logViewModel.LogEntries[0].Message);
    }

    [Fact]
    public void LogViewModel_ShouldAddLogEntries()
    {
        // Arrange
        var mockFileDialogService = new Mock<IFileDialogService>();
        var logViewModel = new LogViewModel(mockFileDialogService.Object);

        // Act
        logViewModel.AddLog(LogLevel.Info, "Test info message");
        logViewModel.AddLog(LogLevel.Warning, "Test warning message");
        logViewModel.AddLog(LogLevel.Error, "Test error message");

        // Assert
        Assert.Equal(3, logViewModel.LogEntries.Count);
        Assert.Equal("Test error message", logViewModel.LogEntries[0].Message); // Latest first
        Assert.Equal(LogLevel.Error, logViewModel.LogEntries[0].Level);
        Assert.Equal("Test warning message", logViewModel.LogEntries[1].Message);
        Assert.Equal("Test info message", logViewModel.LogEntries[2].Message);
    }

    [Fact]
    public void LogViewModel_ShouldLimitMaxEntries()
    {
        // Arrange
        var mockFileDialogService = new Mock<IFileDialogService>();
        var logViewModel = new LogViewModel(mockFileDialogService.Object);

        // Act - Add 1100 entries (max is 1000)
        for (int i = 0; i < 1100; i++)
        {
            logViewModel.AddLog(LogLevel.Info, $"Test message {i}");
        }

        // Assert
        Assert.Equal(1000, logViewModel.LogEntries.Count);
        Assert.Equal("Test message 1099", logViewModel.LogEntries[0].Message); // Latest
        Assert.Equal("Test message 100", logViewModel.LogEntries[999].Message); // Oldest
    }

    [Fact]
    public void LogViewModel_ClearCommand_ShouldClearAllEntries()
    {
        // Arrange
        var mockFileDialogService = new Mock<IFileDialogService>();
        var logViewModel = new LogViewModel(mockFileDialogService.Object);
        logViewModel.AddLog(LogLevel.Info, "Test message 1");
        logViewModel.AddLog(LogLevel.Info, "Test message 2");

        // Act
        logViewModel.ClearCommand.Execute(null);

        // Assert
        Assert.Single(logViewModel.LogEntries); // Only "ログをクリアしました" message
        Assert.Contains("ログをクリアしました", logViewModel.LogEntries[0].Message);
    }

    [Fact]
    public void HostManagerViewModel_ShouldInitializeWithEmptyHosts()
    {
        // Arrange
        var mockHostRegistry = new Mock<IHostRegistry>();
        var mockFileDialogService = new Mock<IFileDialogService>();
        mockHostRegistry.Setup(x => x.Hosts).Returns(new List<Core.Models.OscHost>());

        // Act
        var viewModel = new HostManagerViewModel(mockHostRegistry.Object, mockFileDialogService.Object);

        // Assert
        Assert.NotNull(viewModel.Hosts);
        Assert.Empty(viewModel.Hosts);
    }

    [Fact]
    public void MainViewModel_HasUnsavedChanges_ShouldUpdateWhenProjectServiceChanges()
    {
        // Arrange
        var mockProjectService = new Mock<IProjectService>();
        var mockFileDialogService = new Mock<IFileDialogService>();
        var mockRecentProjectsService = new Mock<IRecentProjectsService>();
        var mockCueManager = new Mock<ICueManager>();
        var mockHostRegistry = new Mock<IHostRegistry>();
        var mockTimecodeRelay = new Mock<ITimecodeRelay>();
        var mockTimecodeEngine = new Mock<ITimecodeEngine>();
        var hostManagerViewModel = new HostManagerViewModel(mockHostRegistry.Object, Mock.Of<IFileDialogService>());
        var logViewModel = new LogViewModel(mockFileDialogService.Object);

        mockRecentProjectsService.Setup(x => x.GetRecentProjects()).Returns(new List<string>());
        mockProjectService.Setup(x => x.HasUnsavedChanges).Returns(false);

        var viewModel = new MainViewModel(
            mockProjectService.Object,
            mockFileDialogService.Object,
            mockRecentProjectsService.Object,
            mockCueManager.Object,
            mockHostRegistry.Object,
            mockTimecodeRelay.Object,
            mockTimecodeEngine.Object,
            new object(),
            new object(),
            new object(),
            hostManagerViewModel,
            logViewModel);

        // Act - Simulate UnsavedChangesStatusChanged event
        mockProjectService.Setup(x => x.HasUnsavedChanges).Returns(true);
        mockProjectService.Raise(x => x.UnsavedChangesStatusChanged += null, EventArgs.Empty);

        // Assert
        Assert.True(viewModel.HasUnsavedChanges);
        Assert.Contains("*", viewModel.Title); // Title should have asterisk for unsaved changes
    }
}
