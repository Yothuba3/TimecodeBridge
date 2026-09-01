using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Core.Services.Interfaces;

public interface ITimecodeEngine
{
    TimecodeValue CurrentRawTimecode { get; }
    TimecodeValue CurrentOffsetTimecode { get; }
    TimecodeOffset Offset { get; set; }
    FrameRate FrameRate { get; set; }
    TimecodeSourceType ActiveSource { get; }
    bool IsReceiving { get; }
    double FreerunDurationSeconds { get; set; }
    bool IsFreerunning { get; }

    void StartLtc(string audioDeviceId, bool isLoopback = false);
    void StartGenerator(GeneratorSettings settings);
    void ResumeGenerator();
    void ResetGenerator();
    void ResetGenerator(TimecodeValue startTime);
    void StopGenerator();
    void Stop();

    event EventHandler<TimecodeUpdatedEventArgs> TimecodeUpdated;
    event EventHandler<TimecodeStatusChangedEventArgs> StatusChanged;
    event EventHandler<AudioSamplesEventArgs> AudioSamplesAvailable;
}
