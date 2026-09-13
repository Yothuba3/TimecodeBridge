using System.Text.Json;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.Host.Bridge;
using TimecodeBridge.Host.Services;

namespace TimecodeBridge.Host.Tests;

/// <summary>実サービス(CueManager/HostRegistry/Panel/Project)とフェイク(エンジン/OSC 送信/デバイス)で Host 側を組み立てる。</summary>
public sealed class HostHarness
{
    public FakeEngine Engine { get; } = new();
    public FakeOscSender Osc { get; } = new();
    public FakeDeviceService Devices { get; } = new();
    public IHostRegistry Hosts { get; } = new HostRegistry();
    public IProjectService Project { get; } = new ProjectService();
    public RecentProjectsStore Recent { get; } = new(Path.Combine(Path.GetTempPath(), $"tcb3-settings-{Guid.NewGuid():N}.json"), Path.Combine(Path.GetTempPath(), "tcb3-no-legacy.json"));
    public ICueManager Cues { get; }
    public IOscTriggerPanelManager Panel { get; }
    public HostState State { get; }
    public ProjectCoordinator Projects { get; }
    public CommandRouter Router { get; }

    public HostHarness()
    {
        Cues = new CueManager(Engine, Osc);
        Panel = new OscTriggerPanelManager(Osc, Hosts);
        State = new HostState(Engine, Cues, Hosts, Osc, Panel, Project, Devices, Recent);
        Projects = new ProjectCoordinator(State, Engine, Cues, Hosts, Panel, Project, Recent);
        Router = new CommandRouter(State, Engine, Cues, Hosts, Panel, Projects);
    }

    public ResultMessage Run(string command, string argsJson = "{}", string requestId = "r1", long? expectedRevision = null)
    {
        var revision = expectedRevision is null ? "" : $$""", "expectedRevision":{{expectedRevision}}""";
        var json = $$"""{"protocolVersion":1,"type":"command","requestId":"{{requestId}}","command":"{{command}}","args":{{argsJson}}{{revision}}}""";
        var msg = JsonSerializer.Deserialize<WebMessage>(json, Protocol.Json)!;
        return Router.ExecuteAsync(msg).GetAwaiter().GetResult();
    }
}
