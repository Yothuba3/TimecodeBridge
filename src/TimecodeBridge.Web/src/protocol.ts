/** Wire contract mirrored from docs/v3/bridge-protocol.md; keep Host Protocol.cs aligned with this file. */
export const PROTOCOL_VERSION = 1 as const;

export type HostMessage =
  | { protocolVersion: 1; type: "snapshot"; revision: number; state: AppState }
  | { protocolVersion: 1; type: "patch"; revision: number; baseRevision: number; changes: StateChanges }
  | { protocolVersion: 1; type: "clock"; seq: number; clock: ClockState }
  | { protocolVersion: 1; type: "wave"; seq: number; wave: WaveState }
  | { protocolVersion: 1; type: "result"; requestId: string; ok: true; data?: unknown; revision?: number }
  | { protocolVersion: 1; type: "result"; requestId: string; ok: false; error: ProtocolError };

export type WebMessage =
  | { protocolVersion: 1; type: "ready"; clientVersion: string; capabilities: string[] }
  | { protocolVersion: 1; type: "command"; requestId: string; command: CommandName; args: unknown; expectedRevision?: number }
  | { protocolVersion: 1; type: "resync"; currentRevision: number | null; reason: string }
  | { protocolVersion: 1; type: "viewport"; waveformWidth: number; devicePixelRatio: number; visible: boolean };

export type FrameRate = "24" | "25" | "29.97df" | "30";

export interface AppState {
  sessionId: string;
  project: { displayName: string; filePath: string | null; dirty: boolean; canUndo: boolean; canRedo: boolean };
  mode: "ltc" | "generate";
  transport: { status: "stopped" | "receiving" | "freerun" | "signalLost" | "error"; statusText: string; detailText: string; signalErrorRatePercent: number | null; locked: boolean; levelVpp: number | null; triggerMuted: boolean; autoMuteEnabled: boolean; autoMutedCueId: string | null; autoUnmuteAtUtc: string | null };
  receive: { selectedDeviceId: string | null; devices: AudioDevice[]; offset: string; triggerWindowFrames: number; freerunDurationSeconds: number };
  generator: { running: boolean; startTime: string; frameRate: FrameRate; selectedOutputDeviceId: string | null; outputDevices: AudioDevice[]; volume: number; ltcOutputActive: boolean; settingsPendingReset: boolean };
  currentClock: ClockState;
  nextCue: NextCueState | null;
  cues: CueDto[];
  hosts: HostDto[];
  cueSync: { oscAddress: string; targetHostIds: string[] };
  relay: { oscAddressPattern: string; interval: { mode: "everyFrame" | "custom"; intervalMs: number }; targetHostIds: string[]; continuousEnabled: boolean };
  triggerPanel: { rows: number; columns: number; buttons: TriggerButtonDto[] };
  logs: LogDto[];
  uiCapabilities: { supportsNativeOpenDialog: boolean; supportsNativeSaveDialog: boolean; platform: "windows" | "macos" };
}

export interface ClockState { raw: string; display: string; totalFrames: number; frameRate: FrameRate; dropFrame: boolean; receivedAtMonotonicMs: number; nextCueId?: string | null; framesUntilNextCue?: number | null }
export interface NextCueState { id: string; name: string; triggerTime: string; effectiveTriggerTime: string; framesUntil: number }
export interface AudioDevice { id: string; name: string; loopback: boolean }
export interface CueDto { id: string; name: string; memo: string; triggerTime: string; effectiveTriggerTime: string; frameRate: FrameRate; oscAddress: string; additionalOscAddresses: string[]; arguments: OscArgumentDto[]; targetHostIds: string[]; enabled: boolean; sendTriggerTimeAsSeconds: boolean; sendTimecode: string | null; triggerOffset: string | null; autoMuteOnFire: boolean; autoUnmuteAfter: string | null; runtime: { lastTriggeredAtUtc: string | null; flashToken: number; muteCountdownText: string } }
export type OscArgumentDto = { type: "int32"; value: number } | { type: "float32"; value: number } | { type: "string"; value: string };
export interface HostDto { id: string; name: string; ipAddress: string; port: number; enabled: boolean; reachability: "unknown" | "checking" | "reachable" | "unreachable" }
export interface TriggerButtonDto { id: string; row: number; column: number; label: string; oscAddress: string; arguments: OscArgumentDto[]; targetHostIds: string[] }
export interface LogDto { id: string; timestampUtc: string; message: string; success: boolean }

export interface StateChanges { project?: AppState["project"]; mode?: AppState["mode"]; transport?: AppState["transport"]; receive?: AppState["receive"]; generator?: AppState["generator"]; nextCue?: NextCueState | null; cues?: CueDto[]; hosts?: HostDto[]; cueSync?: AppState["cueSync"]; relay?: AppState["relay"]; triggerPanel?: AppState["triggerPanel"]; logsAppend?: LogDto[]; logsReset?: LogDto[] }

export interface ProtocolError { code: "badMessage" | "unsupportedVersion" | "unknownCommand" | "validation" | "notFound" | "conflict" | "invalidState" | "deviceNotFound" | "audioError" | "nativeError" | "networkError" | "oscError" | "ioError" | "internal"; message: string; fieldErrors?: Record<string, string>; retryable: boolean; details?: Record<string, unknown> }

/** Commands enumerated by the bridge protocol's JS-to-C# command table. */
export type CommandName =
  | "app.undo" | "app.redo" | "app.requestClose"
  | "project.new" | "project.open" | "project.save" | "project.saveAs"
  | "mode.set"
  | "ltc.start" | "ltc.stop" | "ltc.reconnect"
  | "audio.refreshDevices"
  | "receive.setOffset" | "receive.setTriggerWindow" | "receive.setFreerunDuration"
  | "generator.configure" | "generator.start" | "generator.stop" | "generator.reset"
  | "mute.set" | "autoMute.setEnabled"
  | "cue.add" | "cue.update" | "cue.remove" | "cue.duplicate" | "cue.batchUpdate"
  | "cue.sortByTime" | "cue.setEnabled" | "cue.fire"
  | "cueSync.configure" | "cueSync.send"
  | "host.add" | "host.update" | "host.remove" | "host.setEnabled" | "host.ping"
  | "relay.configure" | "relay.setContinuous" | "relay.sendOnce"
  | "triggerPanel.configureGrid" | "triggerPanel.upsertButton" | "triggerPanel.removeButton" | "triggerPanel.fire"
  | "logs.clear";
/** Interim v1 payload until bridge-protocol.md specifies WaveState. Values are normalized -1..1. */
export interface WaveState { min: number[]; max: number[] }

const object = (value: unknown): value is Record<string, unknown> => typeof value === "object" && value !== null && !Array.isArray(value);
const finite = (value: unknown): value is number => typeof value === "number" && Number.isFinite(value);
export function isClockState(value: unknown): value is ClockState {
  return object(value) && typeof value.raw === "string" && typeof value.display === "string" && finite(value.totalFrames)
    && ["24", "25", "29.97df", "30"].includes(String(value.frameRate)) && typeof value.dropFrame === "boolean" && finite(value.receivedAtMonotonicMs);
}
export function isHostMessage(value: unknown): value is HostMessage {
  if (!object(value) || value.protocolVersion !== 1 || typeof value.type !== "string") return false;
  if (value.type === "clock") return finite(value.seq) && isClockState(value.clock);
  if (value.type === "wave") return finite(value.seq) && object(value.wave) && Array.isArray(value.wave.min) && Array.isArray(value.wave.max) && value.wave.min.every(finite) && value.wave.max.every(finite);
  if (value.type === "snapshot") return finite(value.revision) && object(value.state) && typeof value.state.sessionId === "string" && Array.isArray(value.state.cues) && object(value.state.transport) && isClockState(value.state.currentClock);
  if (value.type === "patch") return finite(value.revision) && finite(value.baseRevision) && object(value.changes);
  if (value.type === "result") return typeof value.requestId === "string" && typeof value.ok === "boolean" && (value.ok || object(value.error));
  return false;
}
export function parseHostMessage(json: string | unknown): HostMessage | null {
  try { const value: unknown = typeof json === "string" ? JSON.parse(json) : json; return isHostMessage(value) ? value : null; } catch { return null; }
}
