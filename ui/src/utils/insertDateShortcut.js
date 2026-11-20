import { formatDateKey } from "./dateUtils.js";

export function insertDateShortcut(view, event, editable, now = () => new Date()) {
  if (!editable || !event.shiftKey || Boolean(event.altKey) === Boolean(event.ctrlKey) ||
      event.metaKey || event.isComposing ||
      event.getModifierState?.("AltGraph") ||
      (event.key?.toLowerCase() !== "d" && event.code !== "KeyD")) return false;
  event.preventDefault();
  // Use the local calendar date, and a normal editor transaction for save/undo.
  view.dispatch(view.state.tr.insertText(formatDateKey(now())).scrollIntoView());
  return true;
}
