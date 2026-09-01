using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
namespace TimecodeBridge.Tests.Models;

using TimecodeBridge.Core.Models;

public class OscArgumentTests
{
    [Fact]
    public void OscInt32_StoresValue()
    {
        var arg = new OscInt32Argument(42);
        Assert.Equal(42, arg.Value);
        Assert.Equal(OscArgumentType.Int32, arg.Type);
    }

    [Fact]
    public void OscFloat32_StoresValue()
    {
        var arg = new OscFloat32Argument(3.14f);
        Assert.Equal(3.14f, arg.Value);
        Assert.Equal(OscArgumentType.Float32, arg.Type);
    }

    [Fact]
    public void OscString_StoresValue()
    {
        var arg = new OscStringArgument("hello");
        Assert.Equal("hello", arg.Value);
        Assert.Equal(OscArgumentType.String, arg.Type);
    }
}

public class OscHostTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var host = new OscHost
        {
            Id = "host-1",
            Name = "Main Console",
            IpAddress = "192.168.1.100",
            Port = 8000,
            IsEnabled = true,
        };

        Assert.Equal("host-1", host.Id);
        Assert.Equal("Main Console", host.Name);
        Assert.Equal("192.168.1.100", host.IpAddress);
        Assert.Equal(8000, host.Port);
        Assert.True(host.IsEnabled);
    }

    [Fact]
    public void IsEnabled_DefaultsToTrue()
    {
        var host = new OscHost
        {
            Id = "h1",
            Name = "Test",
            IpAddress = "127.0.0.1",
            Port = 9000,
        };
        Assert.True(host.IsEnabled);
    }
}

public class CueTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var triggerTime = new TimecodeValue(0, 1, 0, 0, FrameRate.Fps24);
        var cue = new Cue
        {
            Id = "cue-1",
            Name = "Lighting Change",
            Memo = "Transition to blue",
            TriggerTime = triggerTime,
            OscAddress = "/lighting/color",
            Arguments = [new OscStringArgument("blue")],
            TargetHostIds = ["host-1", "host-2"],
            IsEnabled = true,
        };

        Assert.Equal("cue-1", cue.Id);
        Assert.Equal("Lighting Change", cue.Name);
        Assert.Equal("Transition to blue", cue.Memo);
        Assert.Equal(triggerTime, cue.TriggerTime);
        Assert.Equal("/lighting/color", cue.OscAddress);
        Assert.Single(cue.Arguments);
        Assert.Equal(2, cue.TargetHostIds.Count);
        Assert.True(cue.IsEnabled);
    }

    [Fact]
    public void IsEnabled_DefaultsToTrue()
    {
        var cue = new Cue
        {
            Id = "c1",
            Name = "Test",
            TriggerTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps24),
            OscAddress = "/test",
        };
        Assert.True(cue.IsEnabled);
    }

    [Fact]
    public void Arguments_DefaultsToEmpty()
    {
        var cue = new Cue
        {
            Id = "c1",
            Name = "Test",
            TriggerTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps24),
            OscAddress = "/test",
        };
        Assert.Empty(cue.Arguments);
    }

    [Fact]
    public void TargetHostIds_DefaultsToEmpty()
    {
        var cue = new Cue
        {
            Id = "c1",
            Name = "Test",
            TriggerTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps24),
            OscAddress = "/test",
        };
        Assert.Empty(cue.TargetHostIds);
    }
}
