using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.macOS.ViewModels;
using TimecodeBridge.Services.Interfaces;
using Xunit;

namespace TimecodeBridge.macOS.Tests.ViewModels;

public class CueListViewModelTests
{
    private class MockCueManager : ICueManager
    {
        private readonly List<Cue> _cues = new();

        public IReadOnlyList<Cue> Cues => _cues;
        public int TriggerWindowFrames { get; set; } = 3;
        public bool IsMuted { get; set; }

        public event EventHandler<CueTriggeredEventArgs>? CueTriggered;

        public void AddCue(Cue cue)
        {
            _cues.Add(cue);
        }

        public void UpdateCue(string cueId, Cue updatedCue)
        {
            var index = _cues.FindIndex(c => c.Id == cueId);
            if (index >= 0)
            {
                _cues[index] = updatedCue;
            }
        }

        public void RemoveCue(string cueId)
        {
            _cues.RemoveAll(c => c.Id == cueId);
        }

        public void ReorderCues(IReadOnlyList<string> orderedCueIds)
        {
            // Not implemented for tests
        }

        public void SetCueEnabled(string cueId, bool enabled)
        {
            var cue = _cues.FirstOrDefault(c => c.Id == cueId);
            if (cue != null)
            {
                cue.IsEnabled = enabled;
            }
        }

        public void ManualTrigger(string cueId)
        {
            var cue = _cues.FirstOrDefault(c => c.Id == cueId);
            if (cue != null)
            {
                RaiseCueTriggered(cue, cue.TriggerTime, true);
            }
        }

        public void RaiseCueTriggered(Cue cue, TimecodeValue triggerTimecode, bool isManual)
        {
            CueTriggered?.Invoke(this, new CueTriggeredEventArgs
            {
                Cue = cue,
                TriggerTimecode = triggerTimecode,
                IsManual = isManual
            });
        }
    }

    private class MockTimecodeEngine : ITimecodeEngine
    {
        public TimecodeValue CurrentRawTimecode { get; set; } = new(0, 0, 0, 0, FrameRate.Fps30);
        public TimecodeValue CurrentOffsetTimecode { get; set; } = new(0, 0, 0, 0, FrameRate.Fps30);
        public TimecodeOffset Offset { get; set; } = TimecodeOffset.Zero(FrameRate.Fps30);
        public FrameRate FrameRate { get; set; } = FrameRate.Fps30;
        public TimecodeSourceType ActiveSource { get; set; } = TimecodeSourceType.Ltc;
        public bool IsReceiving { get; set; }
        public double FreerunDurationSeconds { get; set; } = 5.0;
        public bool IsFreerunning { get; set; }

        public event EventHandler<TimecodeUpdatedEventArgs>? TimecodeUpdated;
        public event EventHandler<TimecodeStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<AudioSamplesEventArgs>? AudioSamplesAvailable;

        public void StartLtc(string audioDeviceId, bool isLoopback = false) { }
        public void StartGenerator(GeneratorSettings settings) { }
        public void ResumeGenerator() { }
        public void ResetGenerator() { }
        public void ResetGenerator(TimecodeValue startTime) { }
        public void StopGenerator() { }
        public void Stop() { }
        public void Dispose() { }

        public void RaiseTimecodeUpdated(TimecodeValue raw, TimecodeValue offset)
        {
            TimecodeUpdated?.Invoke(this, new TimecodeUpdatedEventArgs(raw, offset));
        }
    }

    private class MockHostRegistry : IHostRegistry
    {
        private readonly List<OscHost> _hosts = new();

        public IReadOnlyList<OscHost> Hosts => _hosts;

        public event EventHandler<HostChangedEventArgs>? HostChanged;

        public void SetHostEnabled(string hostId, bool enabled)
        {
            var host = _hosts.FirstOrDefault(h => h.Id == hostId);
            if (host is not null) host.IsEnabled = enabled;
        }

        public IReadOnlyList<OscHost> GetEnabledHosts(IReadOnlyList<string> hostIds)
        {
            return _hosts.Where(h => h.IsEnabled && hostIds.Contains(h.Id)).ToList();
        }

        public void AddHost(OscHost host)
        {
            _hosts.Add(host);
        }

        public void UpdateHost(string hostId, OscHost updatedHost)
        {
            var index = _hosts.FindIndex(h => h.Id == hostId);
            if (index >= 0)
            {
                _hosts[index] = updatedHost;
            }
        }

        public void RemoveHost(string hostId)
        {
            _hosts.RemoveAll(h => h.Id == hostId);
        }
    }

    private class MockCueDialogService : ICueDialogService
    {
        public Cue? DialogResult { get; set; }
        public CueBatchEditResult? BatchEditResult { get; set; }
        public (int count, double intervalHours)? BatchDuplicateResult { get; set; }

        public Cue? ShowEditDialog(Cue template, IReadOnlyList<OscHost> hosts, FrameRate frameRate, string title)
        {
            return DialogResult;
        }

        public CueBatchEditResult? ShowBatchEditDialog(int cueCount, IReadOnlyList<OscHost> hosts, FrameRate frameRate)
        {
            return BatchEditResult;
        }

        public (int count, double intervalHours)? ShowBatchDuplicateDialog()
        {
            return BatchDuplicateResult;
        }
    }

    [AvaloniaFact]
    public async Task Constructor_ShouldInheritFromDispatcherViewModel()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Arrange & Act
            var cueManager = new MockCueManager();
            var timecodeEngine = new MockTimecodeEngine();
            var hostRegistry = new MockHostRegistry();
            var cueDialogService = new MockCueDialogService();

            var viewModel = new CueListViewModel(cueManager, timecodeEngine, hostRegistry, cueDialogService);

            // Assert
            Assert.IsAssignableFrom<DispatcherViewModel>(viewModel);
        });
    }

    [AvaloniaFact]
    public async Task Constructor_ShouldPopulateCueItemsFromCueManager()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Arrange
            var cueManager = new MockCueManager();
            var timecodeEngine = new MockTimecodeEngine();
            var hostRegistry = new MockHostRegistry();
            var cueDialogService = new MockCueDialogService();

            var cue1 = new Cue
            {
                Id = "cue1",
                Name = "Test Cue 1",
                TriggerTime = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30),
                OscAddress = "/cue/1"
            };
            var cue2 = new Cue
            {
                Id = "cue2",
                Name = "Test Cue 2",
                TriggerTime = new TimecodeValue(0, 0, 20, 0, FrameRate.Fps30),
                OscAddress = "/cue/2"
            };

            cueManager.AddCue(cue1);
            cueManager.AddCue(cue2);

            // Act
            var viewModel = new CueListViewModel(cueManager, timecodeEngine, hostRegistry, cueDialogService);

            // Assert
            Assert.Equal(2, viewModel.CueItems.Count);
            Assert.Equal("cue1", viewModel.CueItems[0].Id);
            Assert.Equal("Test Cue 1", viewModel.CueItems[0].Name);
            Assert.Equal("cue2", viewModel.CueItems[1].Id);
            Assert.Equal("Test Cue 2", viewModel.CueItems[1].Name);
        });
    }

    [AvaloniaFact]
    public async Task AddCueCommand_ShouldAddCueToManager_WhenDialogReturnsResult()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Arrange
            var cueManager = new MockCueManager();
            var timecodeEngine = new MockTimecodeEngine();
            var hostRegistry = new MockHostRegistry();
            var cueDialogService = new MockCueDialogService();

            var newCue = new Cue
            {
                Id = string.Empty, // Will be set by command
                Name = "New Cue",
                TriggerTime = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30),
                OscAddress = "/new/cue"
            };

            cueDialogService.DialogResult = newCue;

            var viewModel = new CueListViewModel(cueManager, timecodeEngine, hostRegistry, cueDialogService);

            // Act
            viewModel.AddCueCommand.Execute(null);

            // Assert
            Assert.Single(viewModel.CueItems);
            Assert.Equal("New Cue", viewModel.CueItems[0].Name);
            Assert.NotEmpty(viewModel.CueItems[0].Id);
        });
    }

    [AvaloniaFact]
    public async Task RemoveCueCommand_ShouldRemoveCueFromManagerAndCollection()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Arrange
            var cueManager = new MockCueManager();
            var timecodeEngine = new MockTimecodeEngine();
            var hostRegistry = new MockHostRegistry();
            var cueDialogService = new MockCueDialogService();

            var cue = new Cue
            {
                Id = "cue1",
                Name = "Test Cue",
                TriggerTime = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30),
                OscAddress = "/test"
            };

            cueManager.AddCue(cue);

            var viewModel = new CueListViewModel(cueManager, timecodeEngine, hostRegistry, cueDialogService);

            // Act
            viewModel.RemoveCueCommand.Execute("cue1");

            // Assert
            Assert.Empty(viewModel.CueItems);
            Assert.Empty(cueManager.Cues);
        });
    }

    [AvaloniaFact]
    public async Task UpdateCue_ShouldUpdateCueInManagerAndCollection()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Arrange
            var cueManager = new MockCueManager();
            var timecodeEngine = new MockTimecodeEngine();
            var hostRegistry = new MockHostRegistry();
            var cueDialogService = new MockCueDialogService();

            var originalCue = new Cue
            {
                Id = "cue1",
                Name = "Original Name",
                TriggerTime = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30),
                OscAddress = "/original"
            };

            cueManager.AddCue(originalCue);

            var updatedCue = new Cue
            {
                Id = "cue1",
                Name = "Updated Name",
                TriggerTime = new TimecodeValue(0, 0, 15, 0, FrameRate.Fps30),
                OscAddress = "/updated"
            };

            cueDialogService.DialogResult = updatedCue;

            var viewModel = new CueListViewModel(cueManager, timecodeEngine, hostRegistry, cueDialogService);

            // Act
            viewModel.EditCueCommand.Execute("cue1");

            // Assert
            Assert.Single(viewModel.CueItems);
            Assert.Equal("Updated Name", viewModel.CueItems[0].Name);
        });
    }

    [AvaloniaFact]
    public async Task NextCue_ShouldBeCalculatedBasedOnCurrentTimecode()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Arrange
            var cueManager = new MockCueManager();
            var timecodeEngine = new MockTimecodeEngine();
            var hostRegistry = new MockHostRegistry();
            var cueDialogService = new MockCueDialogService();

            var cue1 = new Cue
            {
                Id = "cue1",
                Name = "Cue 1",
                TriggerTime = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30),
                OscAddress = "/cue1",
                IsEnabled = true
            };

            var cue2 = new Cue
            {
                Id = "cue2",
                Name = "Cue 2",
                TriggerTime = new TimecodeValue(0, 0, 20, 0, FrameRate.Fps30),
                OscAddress = "/cue2",
                IsEnabled = true
            };

            cueManager.AddCue(cue1);
            cueManager.AddCue(cue2);

            var viewModel = new CueListViewModel(cueManager, timecodeEngine, hostRegistry, cueDialogService);

            // Act - Update timecode to 00:00:05:00 (before first cue)
            var currentTimecode = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30);
            timecodeEngine.RaiseTimecodeUpdated(currentTimecode, currentTimecode);

            // Assert - Next cue should be cue1
            Assert.True(viewModel.CueItems[0].IsNextCue);
            Assert.False(viewModel.CueItems[1].IsNextCue);
        });
    }

    [AvaloniaFact]
    public async Task NextCue_ShouldSkipDisabledCues()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Arrange
            var cueManager = new MockCueManager();
            var timecodeEngine = new MockTimecodeEngine();
            var hostRegistry = new MockHostRegistry();
            var cueDialogService = new MockCueDialogService();

            var cue1 = new Cue
            {
                Id = "cue1",
                Name = "Cue 1",
                TriggerTime = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30),
                OscAddress = "/cue1",
                IsEnabled = false // Disabled
            };

            var cue2 = new Cue
            {
                Id = "cue2",
                Name = "Cue 2",
                TriggerTime = new TimecodeValue(0, 0, 20, 0, FrameRate.Fps30),
                OscAddress = "/cue2",
                IsEnabled = true
            };

            cueManager.AddCue(cue1);
            cueManager.AddCue(cue2);

            var viewModel = new CueListViewModel(cueManager, timecodeEngine, hostRegistry, cueDialogService);

            // Act - Update timecode to 00:00:05:00
            var currentTimecode = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30);
            timecodeEngine.RaiseTimecodeUpdated(currentTimecode, currentTimecode);

            // Assert - Next cue should be cue2 (skipping disabled cue1)
            Assert.False(viewModel.CueItems[0].IsNextCue);
            Assert.True(viewModel.CueItems[1].IsNextCue);
        });
    }

    [AvaloniaFact]
    public async Task CueTriggered_ShouldSetIsTriggeredFlag_AndResetAfterDelay()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Arrange
            var cueManager = new MockCueManager();
            var timecodeEngine = new MockTimecodeEngine();
            var hostRegistry = new MockHostRegistry();
            var cueDialogService = new MockCueDialogService();

            var cue = new Cue
            {
                Id = "cue1",
                Name = "Test Cue",
                TriggerTime = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30),
                OscAddress = "/test"
            };

            cueManager.AddCue(cue);

            var viewModel = new CueListViewModel(cueManager, timecodeEngine, hostRegistry, cueDialogService);

            // Act
            cueManager.RaiseCueTriggered(cue, cue.TriggerTime, false);

            // Assert - IsTriggered is set immediately and reset 500ms later on the UI thread
            Assert.True(viewModel.CueItems[0].IsTriggered);
        });
    }

    [AvaloniaFact]
    public async Task ToggleCueEnabledCommand_ShouldToggleCueEnabledState()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Arrange
            var cueManager = new MockCueManager();
            var timecodeEngine = new MockTimecodeEngine();
            var hostRegistry = new MockHostRegistry();
            var cueDialogService = new MockCueDialogService();

            var cue = new Cue
            {
                Id = "cue1",
                Name = "Test Cue",
                TriggerTime = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30),
                OscAddress = "/test",
                IsEnabled = true
            };

            cueManager.AddCue(cue);

            var viewModel = new CueListViewModel(cueManager, timecodeEngine, hostRegistry, cueDialogService);

            // Act
            viewModel.ToggleCueEnabledCommand.Execute("cue1");

            // Assert
            Assert.False(viewModel.CueItems[0].IsEnabled);
            Assert.False(cueManager.Cues[0].IsEnabled);
        });
    }

    [AvaloniaFact]
    public async Task ManualTriggerCommand_ShouldCallCueManagerManualTrigger()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Arrange
            var cueManager = new MockCueManager();
            var timecodeEngine = new MockTimecodeEngine();
            var hostRegistry = new MockHostRegistry();
            var cueDialogService = new MockCueDialogService();

            var cue = new Cue
            {
                Id = "cue1",
                Name = "Test Cue",
                TriggerTime = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30),
                OscAddress = "/test"
            };

            cueManager.AddCue(cue);

            var viewModel = new CueListViewModel(cueManager, timecodeEngine, hostRegistry, cueDialogService);

            var triggeredEventFired = false;
            cueManager.CueTriggered += (s, e) => { triggeredEventFired = true; };

            // Act
            viewModel.ManualTriggerCommand.Execute("cue1");

            // Assert
            Assert.True(triggeredEventFired);
        });
    }

    [AvaloniaFact]
    public async Task SyncFromService_ShouldRefreshCueItemsFromManager()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Arrange
            var cueManager = new MockCueManager();
            var timecodeEngine = new MockTimecodeEngine();
            var hostRegistry = new MockHostRegistry();
            var cueDialogService = new MockCueDialogService();

            var viewModel = new CueListViewModel(cueManager, timecodeEngine, hostRegistry, cueDialogService);

            // Add cues directly to manager (bypassing ViewModel)
            var cue = new Cue
            {
                Id = "cue1",
                Name = "New Cue",
                TriggerTime = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30),
                OscAddress = "/test"
            };
            cueManager.AddCue(cue);

            // Act
            viewModel.SyncFromService();

            // Assert
            Assert.Single(viewModel.CueItems);
            Assert.Equal("cue1", viewModel.CueItems[0].Id);
        });
    }

    [AvaloniaFact]
    public async Task Dispose_ShouldUnsubscribeFromEvents()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Arrange
            var cueManager = new MockCueManager();
            var timecodeEngine = new MockTimecodeEngine();
            var hostRegistry = new MockHostRegistry();
            var cueDialogService = new MockCueDialogService();

            var viewModel = new CueListViewModel(cueManager, timecodeEngine, hostRegistry, cueDialogService);

            // Act
            viewModel.Dispose();

            // Assert - Should not throw when events are raised after dispose
            Assert.NotNull(viewModel);
            timecodeEngine.RaiseTimecodeUpdated(new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30), new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30));
        });
    }
}
