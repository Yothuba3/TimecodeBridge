/** Global shortcut policy: suppresses actions during IME, editable focus, modal use, and unsafe repeats. */
export function isComposing(event: KeyboardEvent): boolean { return event.isComposing; }
import { isEditable } from "./interactions/focus";
export type ShortcutAction="cueSync"|"toggleMute"|"toggleDrawer"|"closeDrawer"|"selectPrevious"|"selectNext"|"editCue"|"deleteCue";
export function shortcutFor(event:Pick<KeyboardEvent,"key"|"repeat"|"isComposing"|"target">,modalOpen:boolean):ShortcutAction|null {if(event.repeat||event.isComposing||modalOpen||isEditable(event.target))return null;if(event.key===" ")return "cueSync";if(event.key.toLowerCase()==="l")return "toggleMute";if(event.key.toLowerCase()==="p")return "toggleDrawer";if(event.key==="Escape")return "closeDrawer";if(event.key==="ArrowUp")return "selectPrevious";if(event.key==="ArrowDown")return "selectNext";if(event.key==="Enter")return "editCue";if(event.key==="Delete")return "deleteCue";return null}
