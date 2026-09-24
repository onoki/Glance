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
