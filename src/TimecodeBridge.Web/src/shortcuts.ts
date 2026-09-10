/** Global shortcut policy: suppresses actions during IME, editable focus, modal use, and unsafe repeats. */
export function isComposing(event: KeyboardEvent): boolean { return event.isComposing; }
