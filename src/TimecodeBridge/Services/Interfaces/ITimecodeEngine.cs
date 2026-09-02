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

    /// <summary>
    /// 信号喪失を検出したとき、デコーダ・ゲートを自動リセットして受信を復帰しやすくするか。
    /// 無信号が続くとデコーダ内部状態が固着し、信号が戻ってもTCが止まったままになることへの対策。
    /// </summary>
    bool LtcAutoRecoverOnSignalLoss { get; set; }

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
