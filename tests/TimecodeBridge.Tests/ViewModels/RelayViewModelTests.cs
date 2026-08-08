namespace TimecodeBridge.Tests.ViewModels;

using TimecodeBridge.Models;
using TimecodeBridge.Services.Interfaces;
using TimecodeBridge.ViewModels;

// --- Stubs ---

internal class StubTimecodeRelay : ITimecodeRelay
{
    public string OscAddressPattern { get; set; } = "/timecode";
    public RelayInterval ContinuousInterval { get; set; } = new(RelayIntervalMode.EveryFrame, 0);
    public IReadOnlyList<string> TargetHostIds { get; set; } = [];
    public bool IsContinuousEnabled { get; set; }
    public int TriggerOneShotCallCount { get; private set; }

    public void TriggerOneShot()
    {
        TriggerOneShotCallCount++;
    }
}

internal class StubHostRegistryForRelay : IHostRegistry
{
    public IReadOnlyList<OscHost> Hosts { get; set; } = [];

    public void AddHost(OscHost host) { }
    public void UpdateHost(string hostId, OscHost updatedHost) { }
    public void RemoveHost(string hostId) { }
    public void SetHostEnabled(string hostId, bool enabled) { }
    public IReadOnlyList<OscHost> GetEnabledHosts(IReadOnlyList<string> hostIds) => [];

    public event EventHandler<HostChangedEventArgs>? HostChanged;
}

// --- Tests ---

public class RelayViewModelTests
{
    private readonly StubTimecodeRelay _relay = new();
    private readonly StubHostRegistryForRelay _hostRegistry = new();

    private RelayViewModel CreateVm() => new(_relay, _hostRegistry, new StubProjectService());

    // --- OscAddressPattern bidirectional binding ---

    [Fact]
    public void OscAddressPattern_InitializedFromRelay()
    {
        _relay.OscAddressPattern = "/custom";
        var vm = CreateVm();

        Assert.Equal("/custom", vm.OscAddressPattern);
    }

    [Fact]
    public void OscAddressPattern_SetOnVm_UpdatesRelay()
    {
        var vm = CreateVm();

        vm.OscAddressPattern = "/newaddr";

        Assert.Equal("/newaddr", _relay.OscAddressPattern);
    }

    [Fact]
    public void OscAddressPattern_RaisesPropertyChanged()
    {
        var vm = CreateVm();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.OscAddressPattern)) raised = true;
        };

        vm.OscAddressPattern = "/changed";

        Assert.True(raised);
    }

    // --- IsContinuousEnabled bidirectional binding ---

    [Fact]
    public void IsContinuousEnabled_InitializedFromRelay()
    {
        _relay.IsContinuousEnabled = true;
        var vm = CreateVm();

        Assert.True(vm.IsContinuousEnabled);
    }

    [Fact]
    public void IsContinuousEnabled_SetOnVm_UpdatesRelay()
    {
        var vm = CreateVm();

        vm.IsContinuousEnabled = true;

        Assert.True(_relay.IsContinuousEnabled);
    }

    // --- ContinuousInterval bidirectional binding ---

    [Fact]
    public void ContinuousInterval_InitializedFromRelay()
    {
        _relay.ContinuousInterval = new RelayInterval(RelayIntervalMode.Custom, 100);
        var vm = CreateVm();

        Assert.Equal(RelayIntervalMode.Custom, vm.ContinuousInterval.Mode);
        Assert.Equal(100, vm.ContinuousInterval.IntervalMs);
    }

    [Fact]
    public void ContinuousInterval_SetOnVm_UpdatesRelay()
    {
        var vm = CreateVm();

        vm.ContinuousInterval = new RelayInterval(RelayIntervalMode.Custom, 50);

        Assert.Equal(50, _relay.ContinuousInterval.IntervalMs);
    }

    // --- TargetHostIds bidirectional binding ---

    [Fact]
    public void TargetHostIds_InitializedFromRelay()
    {
        _relay.TargetHostIds = ["h1", "h2"];
        var vm = CreateVm();

        Assert.Equal(2, vm.TargetHostIds.Count);
        Assert.Equal("h1", vm.TargetHostIds[0]);
    }

    [Fact]
    public void TargetHostIds_SetOnVm_UpdatesRelay()
    {
        var vm = CreateVm();

        vm.TargetHostIds = ["h3"];

        Assert.Single(_relay.TargetHostIds);
        Assert.Equal("h3", _relay.TargetHostIds[0]);
    }

    // --- ToggleContinuousCommand ---

    [Fact]
    public void ToggleContinuousCommand_TogglesIsContinuousEnabled()
    {
        var vm = CreateVm();
        Assert.False(vm.IsContinuousEnabled);

        vm.ToggleContinuousCommand.Execute(null);

        Assert.True(vm.IsContinuousEnabled);
        Assert.True(_relay.IsContinuousEnabled);
    }

    [Fact]
    public void ToggleContinuousCommand_TogglesBackToFalse()
    {
        _relay.IsContinuousEnabled = true;
        var vm = CreateVm();

        vm.ToggleContinuousCommand.Execute(null);

        Assert.False(vm.IsContinuousEnabled);
        Assert.False(_relay.IsContinuousEnabled);
    }

    // --- TriggerOneShotCommand ---

    [Fact]
    public void TriggerOneShotCommand_CallsTriggerOneShot()
    {
        var vm = CreateVm();

        vm.TriggerOneShotCommand.Execute(null);

        Assert.Equal(1, _relay.TriggerOneShotCallCount);
    }

    [Fact]
    public void TriggerOneShotCommand_MultipleCalls()
    {
        var vm = CreateVm();

        vm.TriggerOneShotCommand.Execute(null);
        vm.TriggerOneShotCommand.Execute(null);
        vm.TriggerOneShotCommand.Execute(null);

        Assert.Equal(3, _relay.TriggerOneShotCallCount);
    }
}
