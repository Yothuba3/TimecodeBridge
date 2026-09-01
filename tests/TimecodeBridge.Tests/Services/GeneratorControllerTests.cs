using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Services;

namespace TimecodeBridge.Tests.Services;

public class GeneratorControllerTests : IDisposable
{
    private readonly GeneratorController _controller;

    public GeneratorControllerTests()
    {
        _controller = new GeneratorController();
    }

    public void Dispose()
    {
        _controller.Dispose();
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        _controller.Dispose();
        _controller.Dispose(); // double-dispose safety
    }

    [Fact]
    public void Pause_BeforeStart_DoesNotThrow()
    {
        _controller.Pause();
    }

    [Fact]
    public void Resume_BeforeStart_DoesNotThrow()
    {
        _controller.Resume();
    }

    [Fact]
    public void Reset_BeforeStart_DoesNotThrow()
    {
        _controller.Reset();
    }

    [Fact]
    public void OnFrameGenerated_DefaultsToNull()
    {
        Assert.Null(_controller.OnFrameGenerated);
    }

    [Fact]
    public void Start_WithNoOutputDevice_DoesNotThrow()
    {
        // Generator should work without an audio output device (graceful degradation)
        var settings = new GeneratorSettings
        {
            FrameRate = FrameRate.Fps30,
            StartTime = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30),
            OutputDeviceId = string.Empty,
            VolumeLevel = 0.8f,
        };

        _controller.Start(settings);

        // Allow a short time for the generator to produce at least one frame
        Thread.Sleep(200);

        _controller.Pause();
    }

    [Fact]
    public void Start_InvokesOnFrameGenerated()
    {
        var frames = new List<TimecodeValue>();
        _controller.OnFrameGenerated = frame => frames.Add(frame);

        var settings = new GeneratorSettings
        {
            FrameRate = FrameRate.Fps30,
            StartTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30),
            OutputDeviceId = string.Empty,
        };

        _controller.Start(settings);

        // Wait for a few frames to be generated
        Thread.Sleep(300);

        _controller.Pause();

        Assert.NotEmpty(frames);
    }

    [Fact]
    public void Start_WithInvalidOutputDevice_ContinuesWithoutAudio()
    {
        // Graceful degradation: invalid device ID should not prevent frame generation
        var frames = new List<TimecodeValue>();
        _controller.OnFrameGenerated = frame => frames.Add(frame);

        var settings = new GeneratorSettings
        {
            FrameRate = FrameRate.Fps30,
            StartTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30),
            OutputDeviceId = "non-existent-device-id",
            VolumeLevel = 0.5f,
        };

        _controller.Start(settings);
        Thread.Sleep(300);
        _controller.Pause();

        Assert.NotEmpty(frames);
    }

    [Fact]
    public void Pause_StopsFrameGeneration()
    {
        var frames = new List<TimecodeValue>();
        _controller.OnFrameGenerated = frame => frames.Add(frame);

        var settings = new GeneratorSettings
        {
            FrameRate = FrameRate.Fps30,
            StartTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30),
            OutputDeviceId = string.Empty,
        };

        _controller.Start(settings);
        Thread.Sleep(200);
        _controller.Pause();

        int countAfterPause = frames.Count;
        Thread.Sleep(200);

        // No significant new frames should have been generated after pause
        Assert.InRange(frames.Count, countAfterPause, countAfterPause + 1);
    }

    [Fact]
    public void Resume_ContinuesFromCurrentPosition()
    {
        var frames = new List<TimecodeValue>();
        _controller.OnFrameGenerated = frame => frames.Add(frame);

        var settings = new GeneratorSettings
        {
            FrameRate = FrameRate.Fps30,
            StartTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30),
            OutputDeviceId = string.Empty,
        };

        _controller.Start(settings);
        Thread.Sleep(200);
        _controller.Pause();

        int countAfterPause = frames.Count;

        _controller.Resume();
        Thread.Sleep(200);
        _controller.Pause();

        Assert.True(frames.Count > countAfterPause);
    }

    [Fact]
    public void Dispose_AfterStart_CleansUpResources()
    {
        var settings = new GeneratorSettings
        {
            FrameRate = FrameRate.Fps30,
            StartTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30),
            OutputDeviceId = string.Empty,
        };

        _controller.Start(settings);
        Thread.Sleep(100);

        // Should not throw
        _controller.Dispose();
    }
}
