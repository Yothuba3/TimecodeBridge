/** High-frequency render scheduler: coalesces latest clock/wave updates to at most one paint callback. */
export type PaintTask = () => void;
