import { formatDateKey } from "./dateUtils.js";

export function insertDateShortcut(view, event, editable, now = () => new Date()) {
  if (!editable || event.ctrlKey || !event.shiftKey || !event.altKey || event.metaKey ||
      event.isComposing || event.key.toLowerCase() !== "d") return false;
  event.preventDefault();
  // Use the local calendar date, and a normal editor transaction for save/undo.
  view.dispatch(view.state.tr.insertText(formatDateKey(now())).scrollIntoView());
  return true;
}
