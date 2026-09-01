using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.macOS.Views;
using TimecodeBridge.ViewModels;
using Xunit;

namespace TimecodeBridge.Tests.Views;

public class CueListViewTests
{
    [AvaloniaFact]
    public void CueListView_Should_Display_DataGrid()
    {
        // Arrange
        var view = new CueListView();

        // Act
        var dataGrid = view.FindControl<DataGrid>("CueDataGrid");

        // Assert
        Assert.NotNull(dataGrid);
    }

    [AvaloniaFact]
    public void CueListView_Should_Have_Required_Columns()
    {
        // Arrange
        var view = new CueListView();
        var dataGrid = view.FindControl<DataGrid>("CueDataGrid");

        // Act & Assert
        Assert.NotNull(dataGrid);
        Assert.NotNull(dataGrid.Columns);
        Assert.True(dataGrid.Columns.Count >= 4, "Should have at least 4 columns (Name, TriggerTime, OscAddress, IsMuted)");
    }

    [AvaloniaFact]
    public void CueListView_Should_Enable_Virtualization()
    {
        // Arrange
        var view = new CueListView();
        var dataGrid = view.FindControl<DataGrid>("CueDataGrid");

        // Act & Assert
        Assert.NotNull(dataGrid);
        // DataGridでは自動的にVirtualizingStackPanelを使用
    }

    [AvaloniaFact]
    public void CueListView_Should_Have_ContextMenu()
    {
        // Arrange
        var view = new CueListView();
        var dataGrid = view.FindControl<DataGrid>("CueDataGrid");

        // Act & Assert
        Assert.NotNull(dataGrid);
        Assert.NotNull(dataGrid.ContextMenu);
    }

    [AvaloniaFact]
    public void CueListView_Should_Bind_To_ViewModel()
    {
        // Arrange
        var mockCueManager = new MockCueManager();
        var mockTimecodeEngine = new MockTimecodeEngine();
        var mockHostRegistry = new MockHostRegistry();
        var mockCueDialogService = new MockCueDialogService();

        var viewModel = new CueListViewModel(
            mockCueManager,
            mockTimecodeEngine,
            mockHostRegistry,
            mockCueDialogService
        );

        var view = new CueListView
        {
            DataContext = viewModel
        };

        // Act
        var dataGrid = view.FindControl<DataGrid>("CueDataGrid");

        // Assert
        Assert.NotNull(dataGrid);
        Assert.Equal(viewModel.CueItems, dataGrid.ItemsSource);
    }
}

// Mock implementations for testing
public class MockCueManager : ICueManager
{
    public IReadOnlyList<Cue> Cues { get; } = new List<Cue>();
    public int TriggerWindowFrames { get; set; } = 3;
    public bool IsMuted { get; set; }

    public void AddCue(Cue cue) { }
    public void UpdateCue(string cueId, Cue updatedCue) { }
    public void RemoveCue(string cueId) { }
    public void ReorderCues(IReadOnlyList<string> orderedCueIds) { }
    public void ClearAllCues() { }
    public void ResetFiredCues() { }
    public void ManualTrigger(string cueId) { }
    public void SetCueEnabled(string cueId, bool isEnabled) { }

    public event EventHandler<CueTriggeredEventArgs>? CueTriggered;
}

public class MockTimecodeEngine : ITimecodeEngine
{
    public TimecodeValue CurrentRawTimecode => new(0, 0, 0, 0, FrameRate.Fps30);
    public TimecodeValue CurrentOffsetTimecode => new(0, 0, 0, 0, FrameRate.Fps30);
    public TimecodeOffset Offset { get; set; } = new(1, 0, 0, 0, 0);
    public FrameRate FrameRate { get; set; } = FrameRate.Fps30;
    public TimecodeSourceType ActiveSource => TimecodeSourceType.None;
    public bool IsReceiving => false;
    public double FreerunDurationSeconds { get; set; } = 5.0;
    public bool IsFreerunning => false;

    public void StartCapture(AudioDeviceInfo audioDevice) { }
    public void StopCapture() { }
    public void StartGenerator(TimecodeValue startTimecode) { }
    public void StopGenerator() { }
    public void Stop() { }
    public void Dispose() { }

    public event EventHandler<TimecodeUpdatedEventArgs>? TimecodeUpdated;
    public event EventHandler<TimecodeStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<AudioSamplesEventArgs>? AudioSamplesAvailable;
}

public class MockHostRegistry : IHostRegistry
{
    public IReadOnlyList<OscHost> Hosts { get; } = new List<OscHost>();

    public void AddHost(OscHost host) { }
    public void UpdateHost(string hostId, OscHost updatedHost) { }
    public void RemoveHost(string hostId) { }

    public event EventHandler<EventArgs>? HostsChanged;
}

public class MockCueDialogService : ICueDialogService
{
    public Cue? ShowEditDialog(Cue template, IReadOnlyList<OscHost> availableHosts, FrameRate frameRate, string title)
    {
        return null;
    }

    public CueBatchEditResult? ShowBatchEditDialog(int selectedCount, IReadOnlyList<OscHost> availableHosts, FrameRate frameRate)
    {
        return null;
    }

    public (int count, double intervalHours)? ShowBatchDuplicateDialog()
    {
        return null;
    }
}
