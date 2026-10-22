/**
 * Global shortcut policy: suppresses actions during IME, editable focus, modal use, and unsafe repeats.
 *
 * 本番中の事故を避けるため、修飾キーなしの単独キーには送信や LIVE/MUTE のような
 * 外に出る操作を割り当てない。Escape と矢印キーは画面内の移動だけなので残す。
 */
import {isEditable} from "./interactions/focus";
export type ShortcutAction="closeDrawer"|"selectPrevious"|"selectNext"|"editCue"|"deleteCue"|"projectNew"|"projectOpen"|"projectSave"|"projectSaveAs"|"undo"|"redo";
type KeyEvent=Pick<KeyboardEvent,"key"|"repeat"|"isComposing"|"target">&Partial<Pick<KeyboardEvent,"metaKey"|"ctrlKey"|"shiftKey">>;
export function shortcutFor(event:KeyEvent,modalOpen:boolean):ShortcutAction|null {if(event.repeat||event.isComposing||modalOpen||isEditable(event.target))return null;const mod=event.metaKey||event.ctrlKey,k=event.key.toLowerCase();if(mod&&k==="n")return "projectNew";if(mod&&k==="o")return "projectOpen";if(mod&&k==="s")return event.shiftKey?"projectSaveAs":"projectSave";if(mod&&k==="z")return event.shiftKey?"redo":"undo";
if(event.ctrlKey&&!event.metaKey&&k==="y")return "redo";if(event.key==="Escape")return "closeDrawer";if(event.key==="ArrowUp")return "selectPrevious";if(event.key==="ArrowDown")return "selectNext";if(event.key==="Enter")return "editCue";if(event.key==="Delete"||event.key==="Backspace")return "deleteCue";return null}
