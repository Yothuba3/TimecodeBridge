using Avalonia.Headless.XUnit;
using TimecodeBridge.Core.Services;
namespace TimecodeBridge.Tests.ViewModels;

using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.App.ViewModels;

// --- Stubs for macOS ViewModel ---

internal class StubHostMgrProjectService : IProjectService
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

internal class StubHostRegistryMac : IHostRegistry
{
    private readonly List<OscHost> _hosts = [];

    public IReadOnlyList<OscHost> Hosts => _hosts.AsReadOnly();

    public event EventHandler<HostChangedEventArgs>? HostChanged;

    public void AddHost(OscHost host)
    {
        _hosts.Add(host);
        HostChanged?.Invoke(this, new HostChangedEventArgs
        {
            HostId = host.Id,
            ChangeType = HostChangeType.Added,
        });
    }

    public void UpdateHost(string hostId, OscHost updatedHost)
    {
        var index = _hosts.FindIndex(h => h.Id == hostId);
        if (index < 0) throw new KeyNotFoundException();
        _hosts[index] = updatedHost;
        HostChanged?.Invoke(this, new HostChangedEventArgs
        {
            HostId = hostId,
            ChangeType = HostChangeType.Updated,
        });
    }

    public void RemoveHost(string hostId)
    {
        var removed = _hosts.RemoveAll(h => h.Id == hostId);
        if (removed == 0) throw new KeyNotFoundException();
        HostChanged?.Invoke(this, new HostChangedEventArgs
        {
            HostId = hostId,
            ChangeType = HostChangeType.Removed,
        });
    }

    public void SetHostEnabled(string hostId, bool enabled)
    {
        var host = _hosts.FirstOrDefault(h => h.Id == hostId)
            ?? throw new KeyNotFoundException();
        host.IsEnabled = enabled;
        HostChanged?.Invoke(this, new HostChangedEventArgs
        {
            HostId = hostId,
            ChangeType = HostChangeType.Updated,
        });
    }

    public IReadOnlyList<OscHost> GetEnabledHosts(IReadOnlyList<string> hostIds)
    {
        return _hosts.Where(h => hostIds.Contains(h.Id) && h.IsEnabled).ToList().AsReadOnly();
    }
}

internal class StubOscSenderMac : IOscSender
{
    public List<string> PingedHostIds { get; } = [];
    public List<(string HostId, int Fps)> IcmpPingCalls { get; } = [];

    public event EventHandler<OscSendResultEventArgs>? SendCompleted;

    public void Send(string oscAddress, IReadOnlyList<OscArgument> arguments, IReadOnlyList<string> targetHostIds) { }

    public void SendPing(string hostId)
    {
        PingedHostIds.Add(hostId);
    }

    public Task SendIcmpPingAsync(string hostId, int framesPerSecond)
    {
        IcmpPingCalls.Add((hostId, framesPerSecond));
        return Task.CompletedTask;
    }

    public void RaiseSendCompleted(OscSendResultEventArgs args)
    {
        SendCompleted?.Invoke(this, args);
    }
}

internal class StubHostDialogServiceMac : IHostDialogService
{
    public OscHost? ShowEditDialog(OscHost template)
    {
        // Auto-confirm: return the template as-is
        return template;
    }
}

internal class StubTimecodeEngineMac : ITimecodeEngine
{
    public TimecodeValue CurrentRawTimecode { get; set; }
    public TimecodeValue CurrentOffsetTimecode { get; set; }
    public TimecodeOffset Offset { get; set; }
    public FrameRate FrameRate { get; set; } = FrameRate.Fps30;
    public TimecodeSourceType ActiveSource { get; set; }
    public bool IsReceiving { get; set; }
    public double FreerunDurationSeconds { get; set; }
    public bool IsFreerunning => false;

    public void StartLtc(string audioDeviceId, bool isLoopback = false) { }
    public void Stop() { }
    public void StartGenerator(GeneratorSettings settings) { }
    public void ResetGenerator() { }
    public void ResetGenerator(TimecodeValue startTime) { }
    public void ResumeGenerator() { }
    public void StopGenerator() { }

    public event EventHandler<TimecodeUpdatedEventArgs>? TimecodeUpdated;
    public event EventHandler<TimecodeStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<AudioSamplesEventArgs>? AudioSamplesAvailable;
}

internal class CancellingHostDialogServiceMac : IHostDialogService
{
    public OscHost? ShowEditDialog(OscHost template) => null;
}

// --- Tests ---

public class HostManagerViewModelMacTests
{
    private readonly StubHostRegistryMac _hostRegistry = new();
    private readonly StubOscSenderMac _oscSender = new();
    private readonly StubTimecodeEngineMac _timecodeEngine = new();
    private readonly StubHostDialogServiceMac _hostDialogService = new();

    // ヘッドレス環境では確認ダイアログを出せないため常に許可する
    private class TestableHostManagerViewModel : HostManagerViewModel
    {
        public TestableHostManagerViewModel(IHostRegistry hostRegistry, IOscSender oscSender, ITimecodeEngine timecodeEngine, IHostDialogService hostDialogService, IProjectService projectService)
            : base(hostRegistry, oscSender, timecodeEngine, hostDialogService, projectService)
        {
        }

        protected override bool ConfirmRemoveHost(OscHost host) => true;
    }

    private HostManagerViewModel CreateVm()
    {
        var vm = new TestableHostManagerViewModel(_hostRegistry, _oscSender, _timecodeEngine, _hostDialogService, new StubHostMgrProjectService());
        return vm;
    }

    // --- Constructor ---

    [AvaloniaFact]
    public void Constructor_InitializesEmptyHosts()
    {
        var vm = CreateVm();
        Assert.NotNull(vm.Hosts);
        Assert.Empty(vm.Hosts);
    }

    [AvaloniaFact]
    public void Constructor_SyncsExistingHosts()
    {
        _hostRegistry.AddHost(new OscHost { Id = "h1", Name = "A", IpAddress = "1.2.3.4", Port = 8000 });
        _hostRegistry.AddHost(new OscHost { Id = "h2", Name = "B", IpAddress = "5.6.7.8", Port = 9000 });

        var vm = CreateVm();

        Assert.Equal(2, vm.Hosts.Count);
        Assert.Equal("h1", vm.Hosts[0].Id);
        Assert.Equal("h2", vm.Hosts[1].Id);
    }

    // --- AddHostCommand ---

    [AvaloniaFact]
    public void AddHostCommand_AddsHostWithDefaults()
    {
        var vm = CreateVm();

        vm.AddHostCommand.Execute(null);

        Assert.Single(vm.Hosts);
        Assert.Equal("New Host", vm.Hosts[0].Name);
        Assert.Equal("127.0.0.1", vm.Hosts[0].IpAddress);
        Assert.Equal(9000, vm.Hosts[0].Port);
    }

    [AvaloniaFact]
    public void AddHostCommand_GeneratesUniqueId()
    {
        var vm = CreateVm();

        vm.AddHostCommand.Execute(null);
        vm.AddHostCommand.Execute(null);

        Assert.Equal(2, vm.Hosts.Count);
        Assert.NotEqual(vm.Hosts[0].Id, vm.Hosts[1].Id);
    }

    // --- RemoveHostCommand ---

    [AvaloniaFact]
    public void RemoveHostCommand_RemovesHost()
    {
        var vm = CreateVm();
        vm.AddHostCommand.Execute(null);
        var hostId = vm.Hosts[0].Id;

        vm.RemoveHostCommand.Execute(hostId);

        Assert.Empty(vm.Hosts);
    }

    // --- ToggleHostEnabledCommand ---

    [AvaloniaFact]
    public void ToggleHostEnabledCommand_TogglesEnabled()
    {
        _hostRegistry.AddHost(new OscHost { Id = "h1", Name = "A", IpAddress = "1.2.3.4", Port = 8000, IsEnabled = true });
        var vm = CreateVm();

        vm.ToggleHostEnabledCommand.Execute("h1");

        // The registry should have been updated
        Assert.False(_hostRegistry.Hosts[0].IsEnabled);
    }

    [AvaloniaFact]
    public void ToggleHostEnabledCommand_TogglesBackToEnabled()
    {
        _hostRegistry.AddHost(new OscHost { Id = "h1", Name = "A", IpAddress = "1.2.3.4", Port = 8000, IsEnabled = false });
        var vm = CreateVm();

        vm.ToggleHostEnabledCommand.Execute("h1");

        Assert.True(_hostRegistry.Hosts[0].IsEnabled);
    }

    // --- PingHostCommand ---

    [AvaloniaFact]
    public async Task PingHostCommand_CallsIcmpPing()
    {
        var vm = CreateVm();

        await vm.PingHostCommand.ExecuteAsync("h1");

        Assert.Single(_oscSender.IcmpPingCalls);
        Assert.Equal("h1", _oscSender.IcmpPingCalls[0].HostId);
        Assert.Equal(30, _oscSender.IcmpPingCalls[0].Fps);
    }

    // --- ObservableCollection sync via HostChanged ---

    [AvaloniaFact]
    public void HostChanged_Added_SyncsObservableCollection()
    {
        var vm = CreateVm();

        _hostRegistry.AddHost(new OscHost { Id = "h1", Name = "A", IpAddress = "1.2.3.4", Port = 8000 });

        Assert.Single(vm.Hosts);
        Assert.Equal("h1", vm.Hosts[0].Id);
    }

    [AvaloniaFact]
    public void HostChanged_Removed_SyncsObservableCollection()
    {
        _hostRegistry.AddHost(new OscHost { Id = "h1", Name = "A", IpAddress = "1.2.3.4", Port = 8000 });
        var vm = CreateVm();

        _hostRegistry.RemoveHost("h1");

        Assert.Empty(vm.Hosts);
    }

    [AvaloniaFact]
    public void HostChanged_Updated_SyncsObservableCollection()
    {
        _hostRegistry.AddHost(new OscHost { Id = "h1", Name = "Old", IpAddress = "1.2.3.4", Port = 8000 });
        var vm = CreateVm();

        _hostRegistry.UpdateHost("h1", new OscHost { Id = "h1", Name = "New", IpAddress = "5.6.7.8", Port = 9000 });

        Assert.Single(vm.Hosts);
        Assert.Equal("New", vm.Hosts[0].Name);
    }

    // --- EditHostCommand via IHostDialogService ---

    [AvaloniaFact]
    public void EditHostCommand_DialogConfirmed_UpdatesHostInRegistry()
    {
        _hostRegistry.AddHost(new OscHost { Id = "h1", Name = "Old", IpAddress = "1.2.3.4", Port = 8000 });
        var vm = CreateVm();

        vm.EditHostCommand.Execute("h1");

        // StubHostDialogService returns template as-is, so host should be updated with same values
        Assert.Equal("Old", _hostRegistry.Hosts[0].Name);
    }

    [AvaloniaFact]
    public void EditHostCommand_DialogCancelled_DoesNotModifyHost()
    {
        _hostRegistry.AddHost(new OscHost { Id = "h1", Name = "Original", IpAddress = "1.2.3.4", Port = 8000 });
        var cancelDialogService = new CancellingHostDialogServiceMac();
        var vm = new HostManagerViewModel(_hostRegistry, _oscSender, _timecodeEngine, cancelDialogService, new StubHostMgrProjectService());

        vm.EditHostCommand.Execute("h1");

        Assert.Equal("Original", _hostRegistry.Hosts[0].Name);
    }

    [AvaloniaFact]
    public void AddHostCommand_DialogCancelled_DoesNotAddHost()
    {
        var cancelDialogService = new CancellingHostDialogServiceMac();
        var vm = new HostManagerViewModel(_hostRegistry, _oscSender, _timecodeEngine, cancelDialogService, new StubHostMgrProjectService());

        vm.AddHostCommand.Execute(null);

        Assert.Empty(vm.Hosts);
    }

    // --- Dispose (event unsubscription) ---

    [AvaloniaFact]
    public void Dispose_UnsubscribesFromHostChanged()
    {
        var vm = CreateVm();
        Assert.Empty(vm.Hosts);

        vm.Dispose();

        // After dispose, adding a host should NOT sync to ViewModel
        _hostRegistry.AddHost(new OscHost { Id = "h1", Name = "After Dispose", IpAddress = "1.2.3.4", Port = 8000 });

        Assert.Empty(vm.Hosts);
    }

    // --- Inheritance from DispatcherViewModel ---

    [AvaloniaFact]
    public void HostManagerViewModel_InheritsFromDispatcherViewModel()
    {
        var vm = CreateVm();
        Assert.IsAssignableFrom<DispatcherViewModel>(vm);
    }
}
