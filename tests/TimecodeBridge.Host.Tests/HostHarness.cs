using System.Text.Json;
using TimecodeBridge.Core.Audio;
using TimecodeBridge.Core.Models;
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

    /// <param name="engine">本物のエンジンで通すときに渡す(そのときフェイクの <see cref="Engine"/> は使われない)</param>
    public HostHarness(ITimecodeEngine? engine = null)
    {
        engine ??= Engine;
        Osc.HostRegistry = Hosts;
        Cues = new CueManager(engine, Osc);
        Panel = new OscTriggerPanelManager(Osc, Hosts);
        State = new HostState(engine, Cues, Hosts, Osc, Panel, Project, new CachedAudioDeviceService(Devices), Recent); // 本番(App)と同じく一覧を使い回す
        Projects = new ProjectCoordinator(State, engine, Cues, Hosts, Panel, Project, Recent);
        Router = new CommandRouter(State, engine, Cues, Hosts, Panel, Projects);
    }

    /// <summary>渡す用の本物のエンジン。入力デバイスは選ばず(LTC 受信は開かない)、内部生成の出力先も「なし」のまま回す</summary>
    public static TimecodeEngine RealEngine() => new(
        FrameRate.Fps30,
        new FakeDeviceService(),
        () => throw new NotSupportedException("このテストでは LTC 受信を開かない"),
        () => throw new NotSupportedException("このテストでは出力デバイスを開かない"));

    public ResultMessage Run(string command, string argsJson = "{}", string requestId = "r1", long? expectedRevision = null) =>
        RunAsync(command, argsJson, requestId, expectedRevision).GetAwaiter().GetResult();

    /// <summary>
    /// 本当に非同期で完了する command(host.ping など)用。<see cref="Run"/> は UI スレッドで同期待ちするため、
    /// 継続が UI スレッドへ戻る command ではデッドロックする(Windows の Ping.SendPingAsync は必ず非同期で完了する)。
    /// </summary>
    public Task<ResultMessage> RunAsync(string command, string argsJson = "{}", string requestId = "r1", long? expectedRevision = null)
    {
        var revision = expectedRevision is null ? "" : $$""", "expectedRevision":{{expectedRevision}}""";
        var json = $$"""{"protocolVersion":1,"type":"command","requestId":"{{requestId}}","command":"{{command}}","args":{{argsJson}}{{revision}}}""";
        var msg = JsonSerializer.Deserialize<WebMessage>(json, Protocol.Json)!;
        return Router.ExecuteAsync(msg);
    }
}
