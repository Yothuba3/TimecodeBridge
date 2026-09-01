using System;
using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.App.ViewModels;
using Xunit;

namespace TimecodeBridge.App.Tests.ViewModels;

public class TimecodeViewModelTests
{
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

        public Action<string, bool>? OnStartLtc { get; set; }
        public Action<GeneratorSettings>? OnStartGenerator { get; set; }
        public Action? OnStop { get; set; }
        public Action? OnStopGenerator { get; set; }

        public void StartLtc(string audioDeviceId, bool isLoopback = false) => OnStartLtc?.Invoke(audioDeviceId, isLoopback);
        public void StartGenerator(GeneratorSettings settings) => OnStartGenerator?.Invoke(settings);
        public void ResumeGenerator() { }
        public void ResetGenerator() { }
        public void ResetGenerator(TimecodeValue startTime) { }
        public void StopGenerator() => OnStopGenerator?.Invoke();
        public void Stop() => OnStop?.Invoke();
        public void Dispose() { }

        public void RaiseTimecodeUpdated(TimecodeValue raw, TimecodeValue offset)
            => TimecodeUpdated?.Invoke(this, new TimecodeUpdatedEventArgs(raw, offset));

        public void RaiseStatusChanged(TimecodeReceiveStatus status)
            => StatusChanged?.Invoke(this, new TimecodeStatusChangedEventArgs(status));

        public void RaiseAudioSamples(float[] samples)
            => AudioSamplesAvailable?.Invoke(this, new AudioSamplesEventArgs(samples));
    }

    private class MockAudioDeviceService : IAudioDeviceService
    {
        public List<AudioDeviceInfo> CaptureDevices { get; } = new();
        public List<AudioDeviceInfo> RenderDevices { get; } = new();

        public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices() => CaptureDevices;
        public IReadOnlyList<AudioDeviceInfo> GetRenderDevices() => RenderDevices;
    }

    private class MockCueManager : ICueManager
    {
        public IReadOnlyList<Cue> Cues { get; } = [];
        public int TriggerWindowFrames { get; set; }
        public bool IsMuted { get; set; }
        public event EventHandler<CueTriggeredEventArgs>? CueTriggered;
        public void AddCue(Cue cue) { }
        public void UpdateCue(string cueId, Cue updatedCue) { }
        public void RemoveCue(string cueId) { }
        public void ReorderCues(IReadOnlyList<string> orderedCueIds) { }
        public void SetCueEnabled(string cueId, bool enabled) { }
        public void ManualTrigger(string cueId) => CueTriggered?.Invoke(this, null!);
        public void ResetTracking() { }
        public void SendCueSync(string oscAddress, IReadOnlyList<string> targetHostIds) { }
    }

    private readonly MockTimecodeEngine _engine = new();
    private readonly MockAudioDeviceService _audioService = new();
    private readonly MockCueManager _cueManager = new();

    private class StubHostRegistry : IHostRegistry
    {
        public IReadOnlyList<OscHost> Hosts => [];
        public event EventHandler<HostChangedEventArgs>? HostChanged;
        public void AddHost(OscHost host) { }
        public void UpdateHost(string hostId, OscHost updatedHost) { }
        public void RemoveHost(string hostId) { }
        public void SetHostEnabled(string hostId, bool enabled) { }
        public IReadOnlyList<OscHost> GetEnabledHosts(IReadOnlyList<string> hostIds) => [];
    }

    private class StubProjectService : IProjectService
    {
        public string? CurrentFilePath => null;
        public bool HasUnsavedChanges => false;
        public event EventHandler<EventArgs>? UnsavedChangesStatusChanged;
        public event EventHandler<EventArgs>? ChangeCommitted;
        public ProjectData LoadProject(string filePath) => new();
        public void SaveProject(string filePath, ProjectData data) { }
        public void MarkAsChanged() { }
        public void Reset() { }
    }

    private TimecodeViewModel CreateVm()
    {
        var projectService = new StubProjectService();
        var cueSync = new CueSyncViewModel(_cueManager, new StubHostRegistry(), projectService);
        return new TimecodeViewModel(_engine, _audioService, _cueManager, projectService, cueSync);
    }

    [AvaloniaFact]
    public void 初期状態_LTCモードで停止表示()
    {
        var vm = CreateVm();

        Assert.True(vm.IsLtcMode);
        Assert.False(vm.IsGeneratorMode);
        Assert.Equal("停止", vm.StatusText);
        Assert.False(vm.IsReceiving);
    }

    [AvaloniaFact]
    public void RefreshAudioDevices_入力と出力を分けて列挙する()
    {
        _audioService.CaptureDevices.Add(new AudioDeviceInfo("in1", "Mic", false));
        _audioService.RenderDevices.Add(new AudioDeviceInfo("out1", "Speaker", false));

        var vm = CreateVm();

        Assert.Equal(["in1"], vm.AudioDevices.Select(d => d.Id));
        Assert.Equal(["out1"], vm.OutputDevices.Select(d => d.Id));
    }

    [AvaloniaFact]
    public void デバイス選択でLTCキャプチャが自動開始される()
    {
        _audioService.CaptureDevices.Add(new AudioDeviceInfo("in1", "Mic", false));
        var vm = CreateVm();

        string? startedId = null;
        _engine.OnStartLtc = (id, _) => startedId = id;

        vm.SelectedDevice = vm.AudioDevices.First();

        Assert.Equal("in1", startedId);
    }

    [AvaloniaFact]
    public void ステータス変更でStatusTextが更新される()
    {
        var vm = CreateVm();

        _engine.RaiseStatusChanged(TimecodeReceiveStatus.Receiving);
        Assert.Equal("受信中", vm.StatusText);
        Assert.True(vm.IsReceiving);

        _engine.RaiseStatusChanged(TimecodeReceiveStatus.Freerunning);
        Assert.Equal("フリーラン", vm.StatusText);

        _engine.RaiseStatusChanged(TimecodeReceiveStatus.NotReceiving);
        Assert.Equal("信号喪失", vm.StatusText);
        Assert.False(vm.IsReceiving);
    }

    [AvaloniaFact]
    public void タイムコード更新でRAWとOFSの表示が更新される()
    {
        var vm = CreateVm();

        var raw = new TimecodeValue(1, 2, 3, 4, FrameRate.Fps30);
        var ofs = new TimecodeValue(2, 2, 3, 4, FrameRate.Fps30);
        _engine.RaiseTimecodeUpdated(raw, ofs);

        Assert.Equal("01:02:03:04", vm.RawTimecodeDisplay);
        Assert.Equal("02:02:03:04", vm.OffsetTimecodeDisplay);
    }

    [AvaloniaFact]
    public void 内部生成モードで開始すると設定付きでジェネレーターが起動する()
    {
        _audioService.RenderDevices.Add(new AudioDeviceInfo("out1", "Speaker", false));
        var vm = CreateVm();
        vm.IsGeneratorMode = true;
        vm.GeneratorStartTime = "10:00:00:00";
        vm.GeneratorFrameRate = FrameRate.Fps25;
        vm.SelectedOutputDevice = vm.OutputDevices.First();

        GeneratorSettings? settings = null;
        _engine.OnStartGenerator = s => settings = s;

        vm.StartGeneratorCommand.Execute(null);

        Assert.NotNull(settings);
        Assert.Equal(FrameRate.Fps25, settings.FrameRate);
        Assert.Equal(new TimecodeValue(10, 0, 0, 0, FrameRate.Fps25), settings.StartTime);
        Assert.Equal("out1", settings.OutputDeviceId);
        Assert.Equal("生成中", vm.StatusText);
        Assert.True(vm.IsGeneratorRunning);
        Assert.True(vm.IsLtcOutputActive);
    }

    [AvaloniaFact]
    public void ジェネレーター停止で一時停止表示になる()
    {
        var vm = CreateVm();
        vm.IsGeneratorMode = true;
        vm.StartGeneratorCommand.Execute(null);

        vm.StopGeneratorCommand.Execute(null);

        Assert.Equal("一時停止", vm.StatusText);
        Assert.False(vm.IsGeneratorRunning);
    }

    [AvaloniaFact]
    public void ミュート切替がCueManagerに伝播する()
    {
        var vm = CreateVm();

        vm.IsTriggerMuted = true;
        Assert.True(_cueManager.IsMuted);

        vm.IsTriggerMuted = false;
        Assert.False(_cueManager.IsMuted);
    }

    [AvaloniaFact]
    public void オフセット文字列の設定がエンジンに反映される()
    {
        var vm = CreateVm();

        vm.OffsetText = "+01:00:00:00";

        Assert.Equal(1, _engine.Offset.Hours);
    }

    [AvaloniaFact]
    public void ソース設定の保存と復元が往復する()
    {
        _audioService.CaptureDevices.Add(new AudioDeviceInfo("in1", "Mic", false));
        _audioService.RenderDevices.Add(new AudioDeviceInfo("out1", "Speaker", false));
        var vm = CreateVm();
        vm.IsGeneratorMode = true;
        vm.GeneratorStartTime = "05:00:00:00";
        vm.GeneratorFrameRate = FrameRate.Fps24;
        vm.SelectedOutputDevice = vm.OutputDevices.First();

        var settings = vm.GetSourceSettings();

        var vm2 = CreateVm();
        vm2.RestoreSourceSettings(settings);

        Assert.True(vm2.IsGeneratorMode);
        Assert.Equal("05:00:00:00", vm2.GeneratorStartTime);
        Assert.Equal(FrameRate.Fps24, vm2.GeneratorFrameRate);
        Assert.Equal("out1", vm2.SelectedOutputDevice?.Id);
    }
}
