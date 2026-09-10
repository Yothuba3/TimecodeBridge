/** State boundary: separates revisioned Host truth from local selection, draft, focus, and layout state. */
import type { AppState } from "./protocol";

export interface RootStore {
  host: { sessionId: string | null; revision: number | null; state: AppState | null; clockSeq: number; waveSeq: number; connected: boolean; resyncing: boolean };
  ui: { selectedCueIds: Set<string>; anchorCueId: string | null; focusedCueId: string | null; cueScrollTop: number; cueColumnWidths: number[]; drawerOpen: boolean; drawerHeight: number };
}
