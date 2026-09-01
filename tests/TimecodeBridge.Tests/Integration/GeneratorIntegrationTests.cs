using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
namespace TimecodeBridge.Tests.Integration;

using TimecodeBridge.Core.Models;
using TimecodeBridge.Services;

public class GeneratorIntegrationTests : IDisposable
{
    private readonly TimecodeEngine _engine;

    public GeneratorIntegrationTests()
    {
        _engine = new TimecodeEngine(FrameRate.Fps30);
    }

    public void Dispose()
    {
        _engine.Dispose();
    }

    [Fact]
    public async Task StartGenerator_RaisesTimecodeUpdatedEvent()
    {
        var tcs = new TaskCompletionSource<TimecodeUpdatedEventArgs>();
        _engine.TimecodeUpdated += (_, e) => tcs.TrySetResult(e);

        var settings = new GeneratorSettings
        {
            FrameRate = FrameRate.Fps30,
            StartTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30),
            OutputDeviceId = "",
            VolumeLevel = 0.8f,
        };

        _engine.StartGenerator(settings);

        var result = await WithTimeout(tcs.Task);
        Assert.NotNull(result);
        Assert.Equal(0, result.RawTimecode.Hours);
    }

    [Fact]
    public async Task StartGenerator_SetsActiveSourceToGenerator()
    {
        var tcs = new TaskCompletionSource<TimecodeUpdatedEventArgs>();
        _engine.TimecodeUpdated += (_, e) => tcs.TrySetResult(e);

        var settings = new GeneratorSettings
        {
            FrameRate = FrameRate.Fps30,
            StartTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30),
        };

        _engine.StartGenerator(settings);

        Assert.Equal(TimecodeSourceType.Generator, _engine.ActiveSource);

        await WithTimeout(tcs.Task);
    }

    [Fact]
    public async Task StartGenerator_StatusChangesToReceiving()
    {
        var tcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (e.IsReceiving) tcs.TrySetResult(e);
        };

        var settings = new GeneratorSettings
        {
            FrameRate = FrameRate.Fps30,
            StartTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30),
        };

        _engine.StartGenerator(settings);

        var result = await WithTimeout(tcs.Task);
        Assert.True(result.IsReceiving);
    }

    [Fact]
    public async Task Stop_AfterGenerator_SetsStatusToNotReceiving()
    {
        // Start generator
        var receivingTcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (e.IsReceiving) receivingTcs.TrySetResult(e);
        };

        var settings = new GeneratorSettings
        {
            FrameRate = FrameRate.Fps30,
            StartTime = new TimecodeValue(0, 0, 0, 0, FrameRate.Fps30),
        };

        _engine.StartGenerator(settings);
        await WithTimeout(receivingTcs.Task);

        // Now stop
        var stoppedTcs = new TaskCompletionSource<TimecodeStatusChangedEventArgs>();
        _engine.StatusChanged += (_, e) =>
        {
            if (!e.IsReceiving) stoppedTcs.TrySetResult(e);
        };

        _engine.Stop();

        var result = await WithTimeout(stoppedTcs.Task);
        Assert.False(result.IsReceiving);
    }

    [Fact]
    public async Task ResetGenerator_RestoresStartTime()
    {
        var startTime = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps30);
        var settings = new GeneratorSettings
        {
            FrameRate = FrameRate.Fps30,
            StartTime = startTime,
        };

        var receivedFrames = new List<TimecodeUpdatedEventArgs>();
        var tcs = new TaskCompletionSource();
        _engine.TimecodeUpdated += (_, e) =>
        {
            receivedFrames.Add(e);
            if (receivedFrames.Count >= 3)
                tcs.TrySetResult();
        };

        _engine.StartGenerator(settings);
        await WithTimeout(tcs.Task);

        _engine.ResetGenerator();

        // Wait a bit for the reset to take effect and new frames to arrive
        var resetTcs = new TaskCompletionSource<TimecodeUpdatedEventArgs>();
        _engine.TimecodeUpdated += (_, e) => resetTcs.TrySetResult(e);
        var resetResult = await WithTimeout(resetTcs.Task);

        // After reset, timecode should be near start time
        Assert.Equal(1, resetResult.RawTimecode.Hours);
    }

    [Fact]
    public async Task LtcEncoder_Decoder_RoundTrip()
    {
        // Encode a timecode value to LTC audio, then decode it and verify
        var original = new TimecodeValue(1, 2, 3, 4, FrameRate.Fps30);
        var encoder = new LtcEncoder();
        int sampleRate = 48000;
        encoder.Initialize(sampleRate, FrameRate.Fps30);
        encoder.EnqueueFrame(original);
        encoder.EnqueueFrame(original);
        encoder.EnqueueFrame(original);

        int fps = 30;
        int samplesPerFrame = sampleRate / fps;
        var buffer = new byte[samplesPerFrame * 3 * 2];
        encoder.Read(buffer, 0, buffer.Length);

        var decoder = new LtcDecoder();
        decoder.Initialize(sampleRate, fps);

        TimecodeValue? decoded = null;
        decoder.FrameDecoded += (_, tc) => decoded = tc;
        decoder.ProcessSamples(buffer, buffer.Length, sampleRate, 16, 1);

        Assert.NotNull(decoded);
        Assert.Equal(original.Hours, decoded.Value.Hours);
        Assert.Equal(original.Minutes, decoded.Value.Minutes);
        Assert.Equal(original.Seconds, decoded.Value.Seconds);
        Assert.Equal(original.Frames, decoded.Value.Frames);
    }

    [Fact]
    public void ProjectData_GeneratorSettings_RoundTrip()
    {
        var options = ProjectData.CreateJsonOptions();
        var original = new ProjectData
        {
            SourceSettings = new TimecodeSourceSettings
            {
                SourceType = TimecodeSourceType.Generator,
                GeneratorSettings = new GeneratorSettings
                {
                    FrameRate = FrameRate.Fps2997Drop,
                    StartTime = new TimecodeValue(1, 0, 0, 0, FrameRate.Fps2997Drop),
                    OutputDeviceId = "test-device",
                    VolumeLevel = 0.7f,
                },
            },
        };

        var json = System.Text.Json.JsonSerializer.Serialize(original, options);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<ProjectData>(json, options)!;

        Assert.Equal(TimecodeSourceType.Generator, deserialized.SourceSettings.SourceType);
        Assert.Equal(FrameRate.Fps2997Drop, deserialized.SourceSettings.GeneratorSettings.FrameRate);
        Assert.Equal(1, deserialized.SourceSettings.GeneratorSettings.StartTime.Hours);
        Assert.Equal("test-device", deserialized.SourceSettings.GeneratorSettings.OutputDeviceId);
        Assert.Equal(0.7f, deserialized.SourceSettings.GeneratorSettings.VolumeLevel);
    }

    private static async Task<T> WithTimeout<T>(Task<T> task, int timeoutMs = 5000)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
        if (completed != task)
            throw new TimeoutException($"Operation did not complete within {timeoutMs}ms.");
        return await task;
    }

    private static async Task WithTimeout(Task task, int timeoutMs = 5000)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
        if (completed != task)
            throw new TimeoutException($"Operation did not complete within {timeoutMs}ms.");
        await task;
    }
}
