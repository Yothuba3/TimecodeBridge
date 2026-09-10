/** NativeWebView transport boundary: receives Host envelopes and owns request correlation/resync. */
import type { HostMessage } from "./protocol";

declare global { interface Window { tcb: { receive(message: HostMessage): void } } }

export function installBridge(): void {
  window.tcb = { receive: (_message) => { /* Message routing is implemented in M1. */ } };
}
