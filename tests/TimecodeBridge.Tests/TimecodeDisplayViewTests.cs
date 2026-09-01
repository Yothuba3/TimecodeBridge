using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.macOS.ViewModels;
using TimecodeBridge.macOS.Views;
using Xunit;

namespace TimecodeBridge.Tests;

/// <summary>
/// TimecodeDisplayViewのテスト
/// Avaloniaコントロールのインスタンス化とデータバインディングを検証
/// </summary>
public class TimecodeDisplayViewTests
{
    [Fact]
    public void TimecodeDisplayView_CanBeInstantiated()
    {
        // Arrange & Act
        var view = new TimecodeDisplayView();

        // Assert
        Assert.NotNull(view);
    }

    [Fact]
    public void TimecodeDisplayView_CanBindToViewModel()
    {
        // Arrange
        var mockEngine = new MockTimecodeEngine();
        var mockDeviceService = new MockAudioDeviceService();
        var viewModel = new TimecodeViewModel(mockEngine, mockDeviceService);
        var view = new TimecodeDisplayView
        {
            DataContext = viewModel
        };

        // Act & Assert
        Assert.NotNull(view.DataContext);
        Assert.Equal(viewModel, view.DataContext);
    }

    [Fact]
    public void TimecodeViewModel_UpdatesCurrentTimecodeDisplay()
    {
        // Arrange
        var mockEngine = new MockTimecodeEngine();
        var mockDeviceService = new MockAudioDeviceService();
        var viewModel = new TimecodeViewModel(mockEngine, mockDeviceService);

        // Act
        mockEngine.SimulateTimecodeUpdate(new TimecodeValue(1, 2, 3, 4, FrameRate.Fps30));

        // Assert
        Assert.Equal("01:02:03:04", viewModel.CurrentTimecodeDisplay);
    }

    [Fact]
    public void TimecodeViewModel_UpdatesIsReceiving()
    {
        // Arrange
        var mockEngine = new MockTimecodeEngine();
        var mockDeviceService = new MockAudioDeviceService();
        var viewModel = new TimecodeViewModel(mockEngine, mockDeviceService);

        // Act
        mockEngine.SimulateStatusChange(true);

        // Assert
        Assert.True(viewModel.IsReceiving);
    }

    #region Mock Classes

    private class MockTimecodeEngine : ITimecodeEngine
    {
        public event EventHandler<TimecodeUpdatedEventArgs>? TimecodeUpdated;
        public event EventHandler<TimecodeStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<AudioSamplesEventArgs>? AudioSamplesAvailable;

        public TimecodeValue CurrentRawTimecode { get; private set; } = TimecodeValue.Zero(FrameRate.Fps30);
        public TimecodeValue CurrentOffsetTimecode { get; private set; } = TimecodeValue.Zero(FrameRate.Fps30);
        public TimecodeOffset Offset { get; set; } = TimecodeOffset.Zero;
        public FrameRate FrameRate { get; set; } = FrameRate.Fps30;
        public TimecodeSourceType ActiveSource { get; private set; } = TimecodeSourceType.None;
        public bool IsReceiving { get; private set; }
        public double FreerunDurationSeconds { get; set; }
        public bool IsFreerunning { get; private set; }

        public void StartLtc(string deviceId, bool isLoopback) { }
        public void Stop() { }

        public void SimulateTimecodeUpdate(TimecodeValue timecode)
        {
            CurrentOffsetTimecode = timecode;
            TimecodeUpdated?.Invoke(this, new TimecodeUpdatedEventArgs(timecode, timecode, TimecodeSourceType.LtcCapture));
        }

        public void SimulateStatusChange(bool isReceiving)
        {
            IsReceiving = isReceiving;
            StatusChanged?.Invoke(this, new TimecodeStatusChangedEventArgs(isReceiving, TimecodeSourceType.LtcCapture));
        }

        public void Dispose() { }
    }

    private class MockAudioDeviceService : IAudioDeviceService
    {
        public AudioDeviceInfo[] GetCaptureDevices()
        {
            return new[]
            {
                new AudioDeviceInfo("mock-device-1", "Mock Audio Device 1", false)
            };
        }

        public AudioDeviceInfo[] GetRenderDevices()
        {
            return Array.Empty<AudioDeviceInfo>();
        }
    }

    #endregion
}
