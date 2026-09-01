using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
namespace TimecodeBridge.Tests.Models;

using TimecodeBridge.Core.Models;

public class TimecodeValueTests
{
    [Theory]
    [InlineData(FrameRate.Fps24, 1, 0, 0, 0, 86400)]
    [InlineData(FrameRate.Fps25, 1, 0, 0, 0, 90000)]
    [InlineData(FrameRate.Fps30, 1, 0, 0, 0, 108000)]
    [InlineData(FrameRate.Fps24, 0, 1, 0, 0, 1440)]
    [InlineData(FrameRate.Fps24, 0, 0, 1, 0, 24)]
    [InlineData(FrameRate.Fps24, 0, 0, 0, 1, 1)]
    [InlineData(FrameRate.Fps24, 0, 0, 0, 0, 0)]
    public void TotalFrames_ReturnsCorrectValue(FrameRate fps, int h, int m, int s, int f, long expected)
    {
        var tc = new TimecodeValue(h, m, s, f, fps);
        Assert.Equal(expected, tc.TotalFrames());
    }

    [Fact]
    public void TotalFrames_2997Drop_CorrectCalculation()
    {
        // 29.97 drop frame: drops frames 0 and 1 at each minute except every 10th minute
        // At 1 minute: 30*60 - 2 = 1798 frames
        var tc = new TimecodeValue(0, 1, 0, 0, FrameRate.Fps2997Drop);
        Assert.Equal(1798, tc.TotalFrames());

        // At 10 minutes: 10 * 1798 + 2 = 17982 (no drop at 10th minute boundary)
        var tc10 = new TimecodeValue(0, 10, 0, 0, FrameRate.Fps2997Drop);
        Assert.Equal(17982, tc10.TotalFrames());

        // At 1 hour
        var tc1h = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps2997Drop);
        Assert.Equal(107892, tc1h.TotalFrames());
    }

    [Fact]
    public void FromTotalFrames_RoundTrip_NonDrop()
    {
        var original = new TimecodeValue(1, 23, 45, 12, FrameRate.Fps24);
        var frames = original.TotalFrames();
        var restored = TimecodeValue.FromTotalFrames(frames, FrameRate.Fps24);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void FromTotalFrames_RoundTrip_DropFrame()
    {
        var original = new TimecodeValue(0, 15, 30, 10, FrameRate.Fps2997Drop);
        var frames = original.TotalFrames();
        var restored = TimecodeValue.FromTotalFrames(frames, FrameRate.Fps2997Drop);
        Assert.Equal(original, restored);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(23, 59, 59, 23)]
    [InlineData(10, 30, 15, 20)]
    public void FromTotalFrames_RoundTrip_AllValues(int h, int m, int s, int f)
    {
        var original = new TimecodeValue(h, m, s, f, FrameRate.Fps24);
        var frames = original.TotalFrames();
        var restored = TimecodeValue.FromTotalFrames(frames, FrameRate.Fps24);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void Add_PositiveOffset()
    {
        var tc = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps24);
        var offset = new TimecodeOffset(false, 0, 0, 10, 12, FrameRate.Fps24);
        var result = tc.Add(offset);

        Assert.Equal(1, result.Hours);
        Assert.Equal(0, result.Minutes);
        Assert.Equal(10, result.Seconds);
        Assert.Equal(12, result.Frames);
    }

    [Fact]
    public void Add_NegativeOffset()
    {
        var tc = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps24);
        var offset = new TimecodeOffset(true, 0, 0, 10, 12, FrameRate.Fps24);
        var result = tc.Add(offset);

        Assert.Equal(0, result.Hours);
        Assert.Equal(59, result.Minutes);
        Assert.Equal(49, result.Seconds);
        Assert.Equal(12, result.Frames);
    }

    [Fact]
    public void Add_NegativeOffset_ClampsToZero()
    {
        var tc = new TimecodeValue(0, 0, 5, 0, FrameRate.Fps24);
        var offset = new TimecodeOffset(true, 0, 0, 10, 0, FrameRate.Fps24);
        var result = tc.Add(offset);

        Assert.Equal(0, result.Hours);
        Assert.Equal(0, result.Minutes);
        Assert.Equal(0, result.Seconds);
        Assert.Equal(0, result.Frames);
    }

    [Fact]
    public void ToString_FormatsCorrectly()
    {
        var tc = new TimecodeValue(1, 2, 3, 4, FrameRate.Fps24);
        Assert.Equal("01:02:03:04", tc.ToString());
    }

    [Fact]
    public void ToString_DropFrame_UsesSemicolon()
    {
        var tc = new TimecodeValue(1, 2, 3, 4, FrameRate.Fps2997Drop);
        Assert.Equal("01:02:03;04", tc.ToString());
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var a = new TimecodeValue(1, 2, 3, 4, FrameRate.Fps24);
        var b = new TimecodeValue(1, 2, 3, 4, FrameRate.Fps24);
        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Equality_DifferentValues_AreNotEqual()
    {
        var a = new TimecodeValue(1, 2, 3, 4, FrameRate.Fps24);
        var b = new TimecodeValue(1, 2, 3, 5, FrameRate.Fps24);
        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void CompareTo_ReturnsCorrectOrder()
    {
        var earlier = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps24);
        var later = new TimecodeValue(0, 0, 20, 0, FrameRate.Fps24);
        Assert.True(earlier.CompareTo(later) < 0);
        Assert.True(later.CompareTo(earlier) > 0);
        Assert.Equal(0, earlier.CompareTo(earlier));
    }

    [Fact]
    public void ComparisonOperators_WorkCorrectly()
    {
        var earlier = new TimecodeValue(0, 0, 10, 0, FrameRate.Fps24);
        var later = new TimecodeValue(0, 0, 20, 0, FrameRate.Fps24);
        Assert.True(earlier < later);
        Assert.True(earlier <= later);
        Assert.True(later > earlier);
        Assert.True(later >= earlier);
    }
}

public class TimecodeOffsetTests
{
    [Fact]
    public void TotalFrames_Positive()
    {
        var offset = new TimecodeOffset(false, 0, 1, 0, 0, FrameRate.Fps24);
        Assert.Equal(1440, offset.TotalFrames());
    }

    [Fact]
    public void TotalFrames_Negative()
    {
        var offset = new TimecodeOffset(true, 0, 1, 0, 0, FrameRate.Fps24);
        Assert.Equal(-1440, offset.TotalFrames());
    }

    [Fact]
    public void ToString_Positive()
    {
        var offset = new TimecodeOffset(false, 1, 2, 3, 4, FrameRate.Fps24);
        Assert.Equal("+01:02:03:04", offset.ToString());
    }

    [Fact]
    public void ToString_Negative()
    {
        var offset = new TimecodeOffset(true, 1, 2, 3, 4, FrameRate.Fps24);
        Assert.Equal("-01:02:03:04", offset.ToString());
    }

    [Fact]
    public void Zero_IsDefault()
    {
        var zero = TimecodeOffset.Zero(FrameRate.Fps24);
        Assert.False(zero.IsNegative);
        Assert.Equal(0, zero.Hours);
        Assert.Equal(0, zero.Minutes);
        Assert.Equal(0, zero.Seconds);
        Assert.Equal(0, zero.Frames);
    }
}

public class FrameRateTests
{
    [Theory]
    [InlineData(FrameRate.Fps24, 24)]
    [InlineData(FrameRate.Fps25, 25)]
    [InlineData(FrameRate.Fps2997Drop, 30)]
    [InlineData(FrameRate.Fps30, 30)]
    public void FramesPerSecond_ReturnsCorrectNominalValue(FrameRate fps, int expected)
    {
        Assert.Equal(expected, fps.FramesPerSecond());
    }

    [Theory]
    [InlineData(FrameRate.Fps24, false)]
    [InlineData(FrameRate.Fps25, false)]
    [InlineData(FrameRate.Fps2997Drop, true)]
    [InlineData(FrameRate.Fps30, false)]
    public void IsDropFrame_ReturnsCorrectly(FrameRate fps, bool expected)
    {
        Assert.Equal(expected, fps.IsDropFrame());
    }
}
