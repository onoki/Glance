import { lastParagraph } from './taskJoin.js';
import { normalizeContent } from './taskUtils.js';

export function horizontalTaskTarget(list, task, direction) {
  const index = list.findIndex(row => row.id === task.id);
  const target = index < 0 ? null : list[index + direction];
  if (!target) return null;
  const content = normalizeContent(target.content);
  const tail = direction < 0 && content.content?.[0]?.content?.length ? lastParagraph(content) : null;
  const position = direction > 0 ? 1 : (tail || lastParagraph(target.title))?.end || 1;
  return { area: tail ? 'content' : 'title', taskId: target.id, selection: { from: position, to: position } };
}

// Position inside the first paragraph of a requested list item, never its closing token.
export function listItemFocusPosition(doc, index, place = 'start') {
  const list = doc.firstChild;
  if (!list || !['bulletList', 'taskList'].includes(list.type.name) || index < 0 || index >= list.childCount) return null;
  let itemStart = 1;
  for (let i = 0; i < index; i++) itemStart += list.child(i).nodeSize;
  const paragraph = list.child(index).firstChild;
  return itemStart + 2 + (place === 'end' ? paragraph.content.size : 0);
}

export function atVerticalDocumentEdge(editor, direction) {
  if (!editor?.state.selection.empty) return false;
  const { $from } = editor.state.selection;
  const end = direction === 'down';
  for (let depth = 0; depth < $from.depth; depth++) {
    if ($from.index(depth) !== (end ? $from.node(depth).childCount - 1 : 0)) return false;
  }
  // Leave wrapped paragraphs and hard-break lines to the native editor until their visual edge.
  return editor.view.endOfTextblock(direction);
}

export function verticalCaretIntent(editor) {
  const { $from } = editor.state.selection;
  if ($from.parentOffset === 0) return { edge: 'start' };
  if ($from.parentOffset === $from.parent.content.size) return { edge: 'end' };
  return { edge: 'middle', x: editor.view.coordsAtPos($from.pos).left };
}

export function verticalFocusPosition(editor, intent) {
  const paragraphs = [];
  editor.state.doc.descendants((node, pos) => {
    if (node.type.name === 'paragraph') paragraphs.push({ start: pos + 1, end: pos + 1 + node.content.size });
  });
  const paragraph = intent.direction > 0 ? paragraphs[0] : paragraphs.at(-1);
  if (!paragraph) return 1;
  if (intent.edge === 'start') return paragraph.start;
  if (intent.edge === 'end') return paragraph.end;
  const edge = intent.direction > 0 ? paragraph.start : paragraph.end;
  const rect = editor.view.coordsAtPos(edge);
  const hit = editor.view.posAtCoords({ left: intent.x, top: (rect.top + rect.bottom) / 2 });
  const position = hit?.pos ?? (intent.x < rect.left ? paragraph.start : paragraph.end);
  return Math.max(paragraph.start, Math.min(paragraph.end, position));
}
