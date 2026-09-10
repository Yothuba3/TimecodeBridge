/** Deterministic browser-side Host simulator for snapshots, patches, latest-only clock/wave, and results. */
import type { HostMessage } from "../protocol";
export type FakeHostSink = (message: HostMessage) => void;
