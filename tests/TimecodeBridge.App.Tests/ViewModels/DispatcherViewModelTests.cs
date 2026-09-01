using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TimecodeBridge.App.ViewModels;
using Xunit;

namespace TimecodeBridge.App.Tests.ViewModels;

public class DispatcherViewModelTests
{
    private class TestDispatcherViewModel : DispatcherViewModel
    {
        public int ActionExecutionCount { get; private set; }
        public bool ActionExecutedOnUIThread { get; private set; }

        public void ExecuteTestAction()
        {
            RunOnUIThread(() =>
            {
                ActionExecutionCount++;
                ActionExecutedOnUIThread = Dispatcher.UIThread.CheckAccess();
            });
        }

        public async Task ExecuteTestActionAsync()
        {
            await RunOnUIThreadAsync(() =>
            {
                ActionExecutionCount++;
                ActionExecutedOnUIThread = Dispatcher.UIThread.CheckAccess();
            });
        }
    }

    [AvaloniaFact]
    public void DispatcherViewModel_ShouldInheritFromObservableObject()
    {
        // Arrange & Act
        var viewModel = new TestDispatcherViewModel();

        // Assert
        Assert.IsAssignableFrom<ObservableObject>(viewModel);
    }

    [AvaloniaFact]
    public void DispatcherViewModel_ShouldImplementIDisposable()
    {
        // Arrange & Act
        var viewModel = new TestDispatcherViewModel();

        // Assert
        Assert.IsAssignableFrom<IDisposable>(viewModel);
    }

    [AvaloniaFact]
    public async Task RunOnUIThread_WhenCalledFromUIThread_ShouldExecuteActionDirectly()
    {
        // Arrange
        var viewModel = new TestDispatcherViewModel();
        var tcs = new TaskCompletionSource<bool>();

        // Act - Execute on UI thread
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            viewModel.ExecuteTestAction();
            tcs.SetResult(true);
        });

        await tcs.Task;

        // Assert
        Assert.Equal(1, viewModel.ActionExecutionCount);
        Assert.True(viewModel.ActionExecutedOnUIThread);
    }

    [AvaloniaFact]
    public async Task RunOnUIThread_WhenCalledFromBackgroundThread_ShouldMarshalToUIThread()
    {
        // Arrange
        var viewModel = new TestDispatcherViewModel();
        var actionExecuted = new TaskCompletionSource<bool>();

        // Act - Execute from background thread
        await Task.Run(async () =>
        {
            viewModel.ExecuteTestAction();

            // Wait for UI thread to process
            await Task.Delay(100);

            // Check execution on UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                actionExecuted.SetResult(viewModel.ActionExecutedOnUIThread);
            });
        });

        var result = await actionExecuted.Task;

        // Assert
        Assert.Equal(1, viewModel.ActionExecutionCount);
        Assert.True(result);
    }

    [AvaloniaFact]
    public async Task RunOnUIThreadAsync_ShouldExecuteActionOnUIThread()
    {
        // Arrange
        var viewModel = new TestDispatcherViewModel();

        // Act
        await viewModel.ExecuteTestActionAsync();

        // Assert
        Assert.Equal(1, viewModel.ActionExecutionCount);
        Assert.True(viewModel.ActionExecutedOnUIThread);
    }

    [AvaloniaFact]
    public async Task RunOnUIThreadAsync_WhenCalledFromBackgroundThread_ShouldMarshalToUIThread()
    {
        // Arrange
        var viewModel = new TestDispatcherViewModel();

        // Act - Execute from background thread
        await Task.Run(async () =>
        {
            await viewModel.ExecuteTestActionAsync();
        });

        // Assert
        Assert.Equal(1, viewModel.ActionExecutionCount);
        Assert.True(viewModel.ActionExecutedOnUIThread);
    }

    [AvaloniaFact]
    public async Task RunOnUIThread_WithMultipleCalls_ShouldExecuteAllActions()
    {
        // Arrange
        var viewModel = new TestDispatcherViewModel();
        const int callCount = 5;

        // Act
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            for (int i = 0; i < callCount; i++)
            {
                viewModel.ExecuteTestAction();
            }
        });

        // Assert
        Assert.Equal(callCount, viewModel.ActionExecutionCount);
    }

    [AvaloniaFact]
    public void Dispose_ShouldBeCallableWithoutErrors()
    {
        // Arrange
        var viewModel = new TestDispatcherViewModel();

        // Act & Assert
        var exception = Record.Exception(() => viewModel.Dispose());
        Assert.Null(exception);
    }

    [AvaloniaFact]
    public void Dispose_MultipleCalls_ShouldBeIdempotent()
    {
        // Arrange
        var viewModel = new TestDispatcherViewModel();

        // Act & Assert
        viewModel.Dispose();
        var exception = Record.Exception(() => viewModel.Dispose());
        Assert.Null(exception);
    }
}
