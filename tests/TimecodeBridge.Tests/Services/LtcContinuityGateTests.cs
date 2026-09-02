namespace TimecodeBridge.Tests.Services;

using TimecodeBridge.Models;
using TimecodeBridge.Services;

public class LtcContinuityGateTests
{
    private static LtcContinuityGate GateCollecting(out List<long> accepted)
    {
        var acc = new List<long>();
        var gate = new LtcContinuityGate();
        gate.FrameAccepted += f => acc.Add(f.ToOrdinal());
        accepted = acc;
        return gate;
    }

    private static TimecodeValue Frame(int h, int m, int s, int f) => new(h, m, s, f, FrameRate.Fps30);

    private static IEnumerable<TimecodeValue> Consecutive(int startFrame, int count)
    {
        long start = Frame(2, 30, 0, startFrame).TotalFrames();
        for (int i = 0; i < count; i++) yield return TimecodeValue.FromTotalFrames(start + i, FrameRate.Fps30);
    }

    [Fact]
    public void 孤立した単発フレームは採用しない()
    {
        var gate = GateCollecting(out var accepted);
        gate.Write(Frame(9, 9, 9, 9));
        Assert.Empty(accepted);
    }

    [Fact]
    public void 連続する2フレームで再ロックしそれ以降を採用する()
    {
        var gate = GateCollecting(out var accepted);
        foreach (var f in Consecutive(0, 10)) gate.Write(f);
        Assert.Equal(10, accepted.Count);
    }

    [Fact]
    public void 位置ジャンプ後も2フレームで再ロックする()
    {
        var gate = GateCollecting(out var accepted);
        gate.Write(Frame(1, 0, 0, 0));
        gate.Write(Frame(1, 0, 0, 1));
        gate.Write(Frame(5, 0, 0, 10));
        gate.Write(Frame(5, 0, 0, 11));
        Assert.Equal(4, accepted.Count);
        Assert.Equal(Frame(5, 0, 0, 11).ToOrdinal(), accepted[^1]);
    }

    [Fact]
    public void 本物とノイズが交互に届いても本物を採用し続ける()
    {
        var gate = GateCollecting(out var accepted);
        var real = Consecutive(0, 12).ToList();
        var realSet = real.Select(f => f.ToOrdinal()).ToHashSet();
        var rng = new Random(1);
        foreach (var f in real)
        {
            gate.Write(f);
            gate.Write(Frame(rng.Next(24), rng.Next(60), rng.Next(60), rng.Next(30)));
        }
        Assert.Equal(12, accepted.Count);
        Assert.All(accepted, o => Assert.Contains(o, realSet));
    }

    [Fact]
    public void Resetすると保留も直前採用も忘れる()
    {
        var gate = GateCollecting(out var accepted);
        gate.Write(Frame(1, 0, 0, 0));
        gate.Reset();
        gate.Write(Frame(1, 0, 0, 1));
        Assert.Empty(accepted);
    }


    [Fact]
    public void 入力数と採用数のカウンタで信号エラー率を計算できる()
    {
        var gate = new LtcContinuityGate();
        var rng = new Random(1);
        foreach (var f in Consecutive(0, 12))
        {
            gate.Write(f);
            gate.Write(Frame(rng.Next(24), rng.Next(60), rng.Next(60), rng.Next(30)));
        }

        Assert.Equal(24, gate.TotalWritten);
        Assert.Equal(12, gate.TotalAccepted);
        long dropped = gate.TotalWritten - gate.TotalAccepted;
        Assert.Equal(50, (int)System.Math.Round(dropped * 100.0 / gate.TotalWritten));
    }

    [Fact]
    public void Resetでカウンタも0に戻る()
    {
        var gate = new LtcContinuityGate();
        foreach (var f in Consecutive(0, 10)) gate.Write(f);
        gate.Reset();

        Assert.Equal(0, gate.TotalWritten);
        Assert.Equal(0, gate.TotalAccepted);
    }
}
