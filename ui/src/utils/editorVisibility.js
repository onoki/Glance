export function revealCaret(editor) {
  if (!editor || editor.isDestroyed || !editor.view.hasFocus()) return;
  let container = editor.view.dom.parentElement;
  while (container && !/(auto|scroll)/.test(getComputedStyle(container).overflowY)) container = container.parentElement;
  if (!container) return;
  const caret = editor.view.coordsAtPos(editor.state.selection.head);
  const bounds = container.getBoundingClientRect();
  const bottom = bounds.top + container.clientHeight;
  if (caret.bottom > bottom - 32) container.scrollTop += caret.bottom - bottom + 32;
  else if (caret.top < bounds.top + 4) container.scrollTop -= bounds.top + 4 - caret.top;
}
