/** Focus/IME policy preserving active drafts across snapshot and keyed list updates. */
export function isEditable(element: EventTarget | null): boolean { return element instanceof HTMLElement && (element.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(element.tagName)); }
