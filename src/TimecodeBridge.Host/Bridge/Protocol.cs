using System.Text.Json;
using System.Text.Json.Serialization;

namespace TimecodeBridge.Host.Bridge;

// Host ⇄ Web の通信型。src/TimecodeBridge.Web/src/protocol.ts と 1:1 で対応させる(docs/v3/bridge-protocol.md)。
// 列挙は言語間で安定する文字列で表す。

public static class Protocol
{
    public const int Version = 1;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };
}

// ---- Host → Web -------------------------------------------------------------

public abstract record HostMessage
{
    [JsonPropertyOrder(-2)] public int ProtocolVersion => Protocol.Version;
    [JsonPropertyOrder(-1)] public abstract string Type { get; }
}

public sealed record SnapshotMessage(long Revision, AppState State) : HostMessage
{
    public override string Type => "snapshot";
}

public sealed record PatchMessage(long Revision, long BaseRevision, StateChanges Changes) : HostMessage
{
    public override string Type => "patch";
}

public sealed record ClockMessage(long Seq, ClockState Clock) : HostMessage
{
    public override string Type => "clock";
}

public sealed record WaveMessage(long Seq, WaveState Wave) : HostMessage
{
    public override string Type => "wave";
}

public sealed record ResultMessage(string RequestId, bool Ok, object? Data = null, long? Revision = null, ProtocolError? Error = null) : HostMessage
{
    public override string Type => "result";

    public static ResultMessage Success(string requestId, object? data = null, long? revision = null) => new(requestId, true, data, revision);
    public static ResultMessage Failure(string requestId, ProtocolError error) => new(requestId, false, Error: error);
}

// ---- Web → Host -------------------------------------------------------------

/// <summary>Web からの生メッセージ。type で分岐してから args を個別に読む。</summary>
public sealed record WebMessage(
    int ProtocolVersion,
    string Type,
    string? RequestId,
    string? Command,
    JsonElement? Args,
    long? ExpectedRevision,
    string? ClientVersion,
    string[]? Capabilities,
    long? CurrentRevision,
    string? Reason,
    int? WaveformWidth,
    double? DevicePixelRatio,
    bool? Visible);

// ---- 状態 -------------------------------------------------------------------

public static class FrameRateCode
{
    public const string Fps24 = "24";
    public const string Fps25 = "25";
    public const string Fps2997Drop = "29.97df";
    public const string Fps30 = "30";
}

public static class TransportStatus
{
    public const string Stopped = "stopped";
    public const string Receiving = "receiving";
    public const string Freerun = "freerun";
    public const string SignalLost = "signalLost";
    public const string Error = "error";
}

public sealed record AppState(
    string SessionId,
    ProjectState Project,
    string Mode,
    TransportState Transport,
    ReceiveState Receive,
    GeneratorState Generator,
    ClockState CurrentClock,
    NextCueState? NextCue,
    IReadOnlyList<CueDto> Cues,
    IReadOnlyList<HostDto> Hosts,
    CueSyncState CueSync,
    RelayState Relay,
    TriggerPanelState TriggerPanel,
    IReadOnlyList<LogDto> Logs,
    UiCapabilities UiCapabilities);

public sealed record ProjectState(string DisplayName, string? FilePath, bool Dirty, bool CanUndo, bool CanRedo);

public sealed record TransportState(
    string Status,
    string StatusText,
    string DetailText,
    double? SignalErrorRatePercent,
    bool Locked,
    double? LevelVpp,
    bool TriggerMuted,
    bool AutoMuteEnabled,
    string? AutoMutedCueId,
    string? AutoUnmuteAtUtc);

public sealed record ReceiveState(
    string? SelectedDeviceId,
    IReadOnlyList<AudioDevice> Devices,
    string Offset,
    int TriggerWindowFrames,
    double FreerunDurationSeconds);

public sealed record GeneratorState(
    bool Running,
    string StartTime,
    string FrameRate,
    string? SelectedOutputDeviceId,
    IReadOnlyList<AudioDevice> OutputDevices,
    double Volume,
    bool LtcOutputActive,
    bool SettingsPendingReset);

public sealed record ClockState(
    string Raw,
    string Display,
    long TotalFrames,
    string FrameRate,
    bool DropFrame,
    double ReceivedAtMonotonicMs,
    string? NextCueId = null,
    long? FramesUntilNextCue = null);

/// <summary>
/// 波形モニター用の縮約データ。Web の viewport 幅に合わせた点数で、各点は表示区間の [min, max] (−1..1)。
/// protocol.ts では未定義(unknown)なので、この形を Codex に共有する。
/// </summary>
public sealed record WaveState(int SampleRate, int WindowMs, float[] Min, float[] Max, double? LevelDbfs);

public sealed record NextCueState(string Id, string Name, string TriggerTime, string EffectiveTriggerTime, long FramesUntil);

public sealed record AudioDevice(string Id, string Name, bool Loopback);

public sealed record CueDto(
    string Id,
    string Name,
    string Memo,
    string TriggerTime,
    string EffectiveTriggerTime,
    string FrameRate,
    string OscAddress,
    IReadOnlyList<string> AdditionalOscAddresses,
    IReadOnlyList<OscArgumentDto> Arguments,
    IReadOnlyList<string> TargetHostIds,
    bool Enabled,
    bool SendTriggerTimeAsSeconds,
    string? SendTimecode,
    string? TriggerOffset,
    bool AutoMuteOnFire,
    string? AutoUnmuteAfter,
    CueRuntime Runtime);

public sealed record CueRuntime(string? LastTriggeredAtUtc, long FlashToken, string MuteCountdownText);

public sealed record OscArgumentDto(string Type, object Value)
{
    public static OscArgumentDto Int32(int v) => new("int32", v);
    public static OscArgumentDto Float32(float v) => new("float32", v);
    public static OscArgumentDto String(string v) => new("string", v);
}

public sealed record HostDto(string Id, string Name, string IpAddress, int Port, bool Enabled, string Reachability);

public sealed record CueSyncState(string OscAddress, IReadOnlyList<string> TargetHostIds);

public sealed record RelayState(string OscAddressPattern, RelayInterval Interval, IReadOnlyList<string> TargetHostIds, bool ContinuousEnabled);

public sealed record RelayInterval(string Mode, int IntervalMs);

public sealed record TriggerPanelState(int Rows, int Columns, IReadOnlyList<TriggerButtonDto> Buttons);

public sealed record TriggerButtonDto(string Id, int Row, int Column, string Label, string OscAddress, IReadOnlyList<OscArgumentDto> Arguments, IReadOnlyList<string> TargetHostIds);

public sealed record LogDto(string Id, string TimestampUtc, string Message, bool Success);

public sealed record UiCapabilities(bool SupportsNativeOpenDialog, bool SupportsNativeSaveDialog, string Platform);

/// <summary>差分パッチ。ドメイン単位で置換する(配列の index 差分は使わない)。null のドメインは変更なし。</summary>
public sealed record StateChanges(
    ProjectState? Project = null,
    string? Mode = null,
    TransportState? Transport = null,
    ReceiveState? Receive = null,
    GeneratorState? Generator = null,
    NextCueState? NextCue = null,
    IReadOnlyList<CueDto>? Cues = null,
    IReadOnlyList<HostDto>? Hosts = null,
    CueSyncState? CueSync = null,
    RelayState? Relay = null,
    TriggerPanelState? TriggerPanel = null,
    IReadOnlyList<LogDto>? LogsAppend = null,
    IReadOnlyList<LogDto>? LogsReset = null);

// ---- エラー -----------------------------------------------------------------

public static class ErrorCode
{
    public const string BadMessage = "badMessage";
    public const string UnsupportedVersion = "unsupportedVersion";
    public const string UnknownCommand = "unknownCommand";
    public const string Validation = "validation";
    public const string NotFound = "notFound";
    public const string Conflict = "conflict";
    public const string InvalidState = "invalidState";
    public const string DeviceNotFound = "deviceNotFound";
    public const string AudioError = "audioError";
    public const string NativeError = "nativeError";
    public const string NetworkError = "networkError";
    public const string OscError = "oscError";
    public const string IoError = "ioError";
    public const string Internal = "internal";
}

public sealed record ProtocolError(
    string Code,
    string Message,
    bool Retryable = false,
    IReadOnlyDictionary<string, string>? FieldErrors = null,
    IReadOnlyDictionary<string, object?>? Details = null);
