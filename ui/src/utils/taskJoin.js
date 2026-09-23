import { emptyContentDoc, mergeTitleDocs, normalizeContent, titleJoinPosition } from './taskUtils.js';

const clone = value => JSON.parse(JSON.stringify(value));
export const nodeSize = node => node.type === 'text' ? node.text.length
  : node.content ? node.content.reduce((size, child) => size + nodeSize(child), node.type === 'doc' ? 0 : 2) : node.type === 'paragraph' ? 2 : 1;
export function lastParagraph(doc) {
  let last = null;
  const visit = (node, position) => {
    if (node.type === 'paragraph') last = { node, end: position + 1 + (node.content || []).reduce((n, child) => n + nodeSize(child), 0) };
    let offset = position + (node.type === 'doc' ? 0 : 1);
    for (const child of node.content || []) { visit(child, offset); offset += nodeSize(child); }
  };
  visit(doc, 0);
  return last;
}
export const isAtDocumentEdge = (editor, end) => {
  if (!editor?.state.selection.empty) return false;
  const { $from } = editor.state.selection;
  if (!$from?.parent) return false;
  if ($from.parentOffset !== (end ? $from.parent.content.size : 0)) return false;
  for (let depth = 0; depth < $from.depth; depth++) {
    if ($from.index(depth) !== (end ? $from.node(depth).childCount - 1 : 0)) return false;
  }
  return true;
};

// Joining task boundaries preserves the upper task's identity and metadata.
export function joinTaskDocuments(upper, lower) {
  const content = clone(normalizeContent(upper.content));
  const upperList = content.content?.[0]?.type === 'bulletList' ? content.content[0] : null;
  const lowerDoc = clone(normalizeContent(lower.content));
  const lowerItems = lowerDoc.content?.[0]?.type === 'bulletList' ? lowerDoc.content[0].content : [];
  if (upperList?.content?.length) {
    const tail = lastParagraph(content);
    const position = tail.end;
    tail.node.content = mergeTitleDocs({type:'doc',content:[tail.node]}, lower.title).content[0].content;
    upperList.content.push(...lowerItems);
    return { title: clone(upper.title), content, area: 'content', selection: {from:position,to:position} };
  }
  const position = titleJoinPosition(upper.title);
  return { title: mergeTitleDocs(upper.title, lower.title), content: lowerItems.length ? lowerDoc : emptyContentDoc(), area:'title', selection:{from:position,to:position} };
}

// Consume one logical line, keeping hard-break tails, later paragraphs and children.
export function joinFirstContentLine(title, rawContent) {
  const content = clone(normalizeContent(rawContent));
  const list = content.content?.[0];
  if (list?.type !== 'bulletList' || !list.content?.length) return null;
  const first = list.content[0];
  const paragraph = first.content[0];
  if (paragraph?.type !== 'paragraph') return null;
  const inline = paragraph.content || [];
  const breakIndex = inline.findIndex(node => node.type === 'hardBreak');
  const consumed = breakIndex < 0 ? inline : inline.slice(0, breakIndex);
  if (breakIndex >= 0) paragraph.content = inline.slice(breakIndex + 1);
  else {
    first.content.shift();
    // Children of the consumed bullet become the first remaining bullets.
    const promoted = [];
    while (first.content[0]?.type === 'bulletList') promoted.push(...first.content.shift().content);
    list.content.splice(0, 1, ...promoted, ...(first.content.length ? [first] : []));
  }
  const position = titleJoinPosition(title);
  return { title:mergeTitleDocs(title,{type:'doc',content:[{type:'paragraph',content:consumed}]}), content:list.content.length ? content : emptyContentDoc(), selection:{from:position,to:position} };
}
