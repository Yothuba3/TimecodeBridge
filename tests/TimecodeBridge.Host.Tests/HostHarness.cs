using System.Text.Json;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.Host.Bridge;

namespace TimecodeBridge.Host.Tests;

/// <summary>実サービス(CueManager/HostRegistry/Relay/Panel/Project)とフェイク(エンジン/OSC 送信/デバイス)で Host 側を組み立てる。</summary>
public sealed class HostHarness
{
    public FakeEngine Engine { get; } = new();
    public FakeOscSender Osc { get; } = new();
    public FakeDeviceService Devices { get; } = new();
    public IHostRegistry Hosts { get; } = new HostRegistry();
    public IProjectService Project { get; } = new ProjectService();
    public ICueManager Cues { get; }
    public ITimecodeRelay Relay { get; }
    public IOscTriggerPanelManager Panel { get; }
    public HostState State { get; }
    public CommandRouter Router { get; }

    public HostHarness()
    {
        Cues = new CueManager(Engine, Osc);
        Relay = new TimecodeRelay(Engine, Osc);
        Panel = new OscTriggerPanelManager(Osc, Hosts);
        State = new HostState(Engine, Cues, Hosts, Osc, Relay, Panel, Project, Devices);
        Router = new CommandRouter(State, Engine, Cues, Hosts, Osc, Relay, Panel);
    }

    public ResultMessage Run(string command, string argsJson = "{}", string requestId = "r1")
    {
        var json = $$"""{"protocolVersion":1,"type":"command","requestId":"{{requestId}}","command":"{{command}}","args":{{argsJson}}}""";
        var msg = JsonSerializer.Deserialize<WebMessage>(json, Protocol.Json)!;
        return Router.Execute(msg);
    }
}
