namespace TimecodeBridge.Tests.Services;

using TimecodeBridge.Models;
using TimecodeBridge.Services;
using TimecodeBridge.Services.Interfaces;

public class OscTriggerPanelManagerTests
{
    private static OscTriggerButton CreateButton(
        string id = "btn-1", int row = 0, int column = 0,
        string oscAddress = "/pon", params string[] targetHostIds)
    {
        return new OscTriggerButton
        {
            Id = id,
            Row = row,
            Column = column,
            Label = "B",
            OscAddress = oscAddress,
            Arguments = [new OscInt32Argument(1)],
            TargetHostIds = targetHostIds.ToList(),
        };
    }

    // --- Trigger ---

    [Fact]
    public void Trigger_ConfiguredButtonWithEnabledHost_SendsOsc()
    {
        var sender = new SpyOscSender();
        var hosts = new StubHostRegistry();
        hosts.AddEnabled("host1");
        var manager = new OscTriggerPanelManager(sender, hosts);
        manager.UpsertButton(CreateButton(oscAddress: "/fire", targetHostIds: "host1"));

        var result = manager.Trigger("btn-1");

        Assert.True(result.Sent);
        Assert.Equal(TriggerSkipReason.None, result.Reason);
        var call = Assert.Single(sender.SendCalls);
        Assert.Equal("/fire", call.OscAddress);
        Assert.Equal("host1", call.TargetHostIds[0]);
    }

    [Fact]
    public void Trigger_UnknownButtonId_DoesNotSend_ReturnsNotConfigured()
    {
        var sender = new SpyOscSender();
        var manager = new OscTriggerPanelManager(sender, new StubHostRegistry());

        var result = manager.Trigger("missing");

        Assert.False(result.Sent);
        Assert.Equal(TriggerSkipReason.NotConfigured, result.Reason);
        Assert.Empty(sender.SendCalls);
    }

    [Fact]
    public void Trigger_EmptyOscAddress_DoesNotSend_ReturnsNotConfigured()
    {
        var sender = new SpyOscSender();
        var hosts = new StubHostRegistry();
        hosts.AddEnabled("host1");
        var manager = new OscTriggerPanelManager(sender, hosts);
        manager.UpsertButton(CreateButton(oscAddress: "", targetHostIds: "host1"));

        var result = manager.Trigger("btn-1");

        Assert.False(result.Sent);
        Assert.Equal(TriggerSkipReason.NotConfigured, result.Reason);
        Assert.Empty(sender.SendCalls);
    }

    [Fact]
    public void Trigger_NoEnabledTarget_DoesNotSend_ReturnsNoEnabledTarget()
    {
        var sender = new SpyOscSender();
        var hosts = new StubHostRegistry(); // host1 は登録しない＝無効扱い
        var manager = new OscTriggerPanelManager(sender, hosts);
        manager.UpsertButton(CreateButton(targetHostIds: "host1"));

        var result = manager.Trigger("btn-1");

        Assert.False(result.Sent);
        Assert.Equal(TriggerSkipReason.NoEnabledTarget, result.Reason);
        Assert.Empty(sender.SendCalls);
    }

    [Fact]
    public void Trigger_NoTargetHostsAtAll_ReturnsNoEnabledTarget()
    {
        var sender = new SpyOscSender();
        var manager = new OscTriggerPanelManager(sender, new StubHostRegistry());
        manager.UpsertButton(CreateButton()); // TargetHostIds 空

        var result = manager.Trigger("btn-1");

        Assert.False(result.Sent);
        Assert.Equal(TriggerSkipReason.NoEnabledTarget, result.Reason);
    }

    // --- SetGridSize ---

    [Fact]
    public void SetGridSize_BelowMinimum_ClampedToOne()
    {
        var manager = new OscTriggerPanelManager(new SpyOscSender(), new StubHostRegistry());

        manager.SetGridSize(0, -5);

        Assert.Equal(1, manager.Rows);
        Assert.Equal(1, manager.Columns);
    }

    [Fact]
    public void SetGridSize_Shrink_RemovesOutOfRangeButtons()
    {
        var manager = new OscTriggerPanelManager(new SpyOscSender(), new StubHostRegistry());
        manager.SetGridSize(4, 4);
        manager.UpsertButton(CreateButton("a", 0, 0));
        manager.UpsertButton(CreateButton("b", 3, 3));

        manager.SetGridSize(2, 2);

        Assert.Single(manager.Buttons);
        Assert.Equal("a", manager.Buttons[0].Id);
    }

    [Fact]
    public void GetOutOfRangeButtons_ListsButtonsBeyondNewSize_WithoutMutating()
    {
        var manager = new OscTriggerPanelManager(new SpyOscSender(), new StubHostRegistry());
        manager.SetGridSize(4, 4);
        manager.UpsertButton(CreateButton("a", 0, 0));
        manager.UpsertButton(CreateButton("b", 3, 1));

        var outOfRange = manager.GetOutOfRangeButtons(2, 2);

        Assert.Single(outOfRange);
        Assert.Equal("b", outOfRange[0].Id);
        Assert.Equal(2, manager.Buttons.Count); // 状態は変わらない
    }

    // --- Upsert / single occupancy ---

    [Fact]
    public void UpsertButton_SameCellDifferentId_ReplacesOccupant()
    {
        var manager = new OscTriggerPanelManager(new SpyOscSender(), new StubHostRegistry());
        manager.UpsertButton(CreateButton("a", 1, 1));
        manager.UpsertButton(CreateButton("b", 1, 1));

        var atCell = manager.GetButtonAt(1, 1);
        Assert.NotNull(atCell);
        Assert.Equal("b", atCell.Id);
        Assert.Single(manager.Buttons);
    }

    [Fact]
    public void UpsertButton_SameId_UpdatesInPlace()
    {
        var manager = new OscTriggerPanelManager(new SpyOscSender(), new StubHostRegistry());
        manager.UpsertButton(CreateButton("a", 1, 1, oscAddress: "/old"));
        manager.UpsertButton(CreateButton("a", 1, 1, oscAddress: "/new"));

        Assert.Single(manager.Buttons);
        Assert.Equal("/new", manager.GetButtonAt(1, 1)!.OscAddress);
    }

    [Fact]
    public void RemoveButton_RemovesById()
    {
        var manager = new OscTriggerPanelManager(new SpyOscSender(), new StubHostRegistry());
        manager.UpsertButton(CreateButton("a", 0, 0));

        manager.RemoveButton("a");

        Assert.Empty(manager.Buttons);
    }

    // --- Persistence ---

    [Fact]
    public void GetSettings_LoadSettings_RoundTrip()
    {
        var manager = new OscTriggerPanelManager(new SpyOscSender(), new StubHostRegistry());
        manager.SetGridSize(3, 6);
        manager.UpsertButton(CreateButton("a", 2, 5, oscAddress: "/x", targetHostIds: "h1"));

        var settings = manager.GetSettings();

        var restored = new OscTriggerPanelManager(new SpyOscSender(), new StubHostRegistry());
        restored.LoadSettings(settings);

        Assert.Equal(3, restored.Rows);
        Assert.Equal(6, restored.Columns);
        var btn = Assert.Single(restored.Buttons);
        Assert.Equal("a", btn.Id);
        Assert.Equal(2, btn.Row);
        Assert.Equal(5, btn.Column);
    }

    [Fact]
    public void Clear_ResetsToDefaults()
    {
        var manager = new OscTriggerPanelManager(new SpyOscSender(), new StubHostRegistry());
        manager.SetGridSize(2, 2);
        manager.UpsertButton(CreateButton("a", 0, 0));

        manager.Clear();

        Assert.Equal(OscTriggerPanelManager.DefaultRows, manager.Rows);
        Assert.Equal(OscTriggerPanelManager.DefaultColumns, manager.Columns);
        Assert.Empty(manager.Buttons);
    }

    [Fact]
    public void Changed_FiresOnUpsert()
    {
        var manager = new OscTriggerPanelManager(new SpyOscSender(), new StubHostRegistry());
        var fired = 0;
        manager.Changed += (_, _) => fired++;

        manager.UpsertButton(CreateButton("a", 0, 0));

        Assert.Equal(1, fired);
    }

    // --- Test Doubles ---

    private sealed class SpyOscSender : IOscSender
    {
        public List<OscSendCall> SendCalls { get; } = [];

        public void Send(string oscAddress, IReadOnlyList<OscArgument> arguments, IReadOnlyList<string> targetHostIds)
            => SendCalls.Add(new OscSendCall(oscAddress, arguments, targetHostIds));

        public void SendPing(string hostId) { }
        public Task SendIcmpPingAsync(string hostId, int framesPerSecond) => Task.CompletedTask;
        public event EventHandler<OscSendResultEventArgs>? SendCompleted;

        public record OscSendCall(string OscAddress, IReadOnlyList<OscArgument> Arguments, IReadOnlyList<string> TargetHostIds);
    }

    private sealed class StubHostRegistry : IHostRegistry
    {
        private readonly List<OscHost> _hosts = [];

        public void AddEnabled(string id) => _hosts.Add(new OscHost
        {
            Id = id, Name = id, IpAddress = "127.0.0.1", Port = 8000, IsEnabled = true,
        });

        public IReadOnlyList<OscHost> Hosts => _hosts;
        public void AddHost(OscHost host) => _hosts.Add(host);
        public void UpdateHost(string hostId, OscHost updatedHost) { }
        public void RemoveHost(string hostId) { }
        public void SetHostEnabled(string hostId, bool enabled) { }

        public IReadOnlyList<OscHost> GetEnabledHosts(IReadOnlyList<string> hostIds)
            => _hosts.Where(h => hostIds.Contains(h.Id) && h.IsEnabled).ToList();

        public event EventHandler<HostChangedEventArgs>? HostChanged;
    }
}
