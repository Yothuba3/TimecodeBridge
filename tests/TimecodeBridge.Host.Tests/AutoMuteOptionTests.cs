using Avalonia.Headless.XUnit;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Host.Bridge;
using Xunit;

namespace TimecodeBridge.Host.Tests;

/// <summary>
/// オートミュートの 2 つの設定。全体の入切(キューリストのツールバー)と、
/// 手動発火でも掛けるか(オプション画面)。手動は進行の確認や差し込みで叩くことが多く、
/// そこでミュートされると後続の自動発火が止まるため、既定では掛けない。
/// </summary>
public class AutoMuteOptionTests
{
    private static HostHarness WithMutingCue()
    {
        var h = new HostHarness();
        h.Hosts.AddHost(new OscHost { Id = "h1", Name = "QLab", IpAddress = "127.0.0.1", Port = 9000 });
        h.Cues.AddCue(new Cue
        {
            Id = "c1",
            Name = "ミュート付き",
            TriggerTime = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30),
            OscAddress = "/go",
            TargetHostIds = ["h1"],
            AutoMuteOnFire = true,
        });
        return h;
    }

    [Fact]
    public void 手動発火は既定ではミュートしない()
    {
        var h = WithMutingCue();
        Assert.False(h.Cues.AutoMuteOnManualTrigger);

        h.Cues.ManualTrigger("c1");

        Assert.False(h.Cues.IsMuted);
    }

    [Fact]
    public void 設定を入れると手動発火でもミュートする()
    {
        var h = WithMutingCue();
        h.Run("autoMute.setOnManualTrigger", """{"enabled":true}""");

        h.Cues.ManualTrigger("c1");

        Assert.True(h.Cues.IsMuted);
    }

    [Fact]
    public void 全体を切ると手動でも自動でもミュートしない()
    {
        var h = WithMutingCue();
        h.Run("autoMute.setOnManualTrigger", """{"enabled":true}""");
        h.Run("autoMute.setEnabled", """{"enabled":false}""");

        h.Cues.ManualTrigger("c1");

        Assert.False(h.Cues.IsMuted);
    }

    [AvaloniaFact]
    public void 設定は状態に乗って画面へ伝わる()
    {
        var h = WithMutingCue();
        h.Run("autoMute.setOnManualTrigger", """{"enabled":true}""");

        var transport = h.State.BuildSnapshot().Transport;

        Assert.True(transport.AutoMuteOnManualTrigger);
        Assert.True(transport.AutoMuteEnabled);
    }

    [Fact]
    public void 不正な引数は受け付けない()
    {
        var h = new HostHarness();

        var result = h.Run("autoMute.setOnManualTrigger", """{"enabled":"yes"}""");

        Assert.NotNull(result.Error);
    }

    [AvaloniaFact]
    public void メニューのオプションは一度きりの合図としてpatchに乗る()
    {
        var h = new HostHarness();
        var seen = new List<StateChanges>();
        h.State.Changed += (c, _, _) => seen.Add(c);

        h.Run("app.openOptions");
        h.State.Flush();
        h.State.MarkDirty(Domain.Receive);
        h.State.Flush();

        Assert.True(seen[0].OpenOptions);
        // 二度目には乗らない(開き直しを繰り返さない)
        Assert.Null(seen[1].OpenOptions);
    }
}
