/** Pure validation/normalization for timecode, OSC, addresses, ports, and numeric UI drafts. */
export type ValidationResult<T> = { ok: true; value: T } | { ok: false; message: string };
