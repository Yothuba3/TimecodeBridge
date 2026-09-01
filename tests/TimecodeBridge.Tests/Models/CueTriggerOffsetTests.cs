using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
namespace TimecodeBridge.Tests.Models;

using TimecodeBridge.Core.Models;

public class CueTriggerOffsetTests
{
    [Fact]
    public void TryApplyTriggerOffset_NoOffset_ReturnsTriggerTime()
    {
        var tc = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps30);

        Assert.True(Cue.TryApplyTriggerOffset(tc, null, out var effective));
        Assert.Equal(tc, effective);
    }

    [Fact]
    public void TryApplyTriggerOffset_NegativeBeyondZero_ReturnsFalse()
    {
        var tc = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30);
        var offset = new TimecodeOffset(true, 0, 0, 10, 0, FrameRate.Fps30); // -10秒

        Assert.False(Cue.TryApplyTriggerOffset(tc, offset, out _));
    }

    [Fact]
    public void TryApplyTriggerOffset_BeyondMidnight_ReturnsFalse()
    {
        var tc = new TimecodeValue(23, 59, 59, 0, FrameRate.Fps30);
        var offset = new TimecodeOffset(false, 0, 0, 2, 0, FrameRate.Fps30); // +2秒 → 24時超

        Assert.False(Cue.TryApplyTriggerOffset(tc, offset, out _));
    }

    [Fact]
    public void TryApplyTriggerOffset_DifferentFrameRate_NormalizedToTriggerTimeFps()
    {
        // 24fpsのキューに30fps環境で作られた「+1秒」を適用しても、ちょうど1秒進む
        var tc = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps24);
        var offset = new TimecodeOffset(false, 0, 0, 1, 0, FrameRate.Fps30);

        Assert.True(Cue.TryApplyTriggerOffset(tc, offset, out var effective));
        Assert.Equal(new TimecodeValue(0, 0, 11, 0, FrameRate.Fps24), effective);
    }

    [Fact]
    public void GetEffectiveTriggerTime_OutOfRangeOffset_FallsBackToTriggerTime()
    {
        var cue = new Cue
        {
            Id = "c1",
            Name = "Test",
            TriggerTime = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps30),
            OscAddress = "/test",
            TriggerOffset = new TimecodeOffset(true, 1, 0, 0, 0, FrameRate.Fps30), // -1時間 → 範囲外
        };

        Assert.Equal(cue.TriggerTime, cue.GetEffectiveTriggerTime());
    }
}
