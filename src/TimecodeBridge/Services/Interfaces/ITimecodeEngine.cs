using TimecodeBridge.Models;

namespace TimecodeBridge.Services.Interfaces;

/// <summary>
/// LTCデコードの累積カウント。デコーダが出したフレーム数と、連続性ゲートが採用した数。
/// 2時点の差分から「信号エラー率」（採用されなかったフレームの割合）を求める。
/// </summary>
public readonly record struct LtcSignalCounts(long Written, long Accepted);

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

    /// <summary>LTCデコードの累積カウント（信号エラー率の算出用）。</summary>
    LtcSignalCounts LtcSignalCounts { get; }


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
