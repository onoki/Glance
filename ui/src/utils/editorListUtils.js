import { TextSelection } from "prosemirror-state";

export const getListItemDepth = (editor) => {
  if (!editor) {
    return null;
  }
  const { $from } = editor.state.selection;
  for (let depth = $from.depth; depth > 0; depth -= 1) {
    if ($from.node(depth).type.name === "listItem") {
      return depth;
    }
  }
  return null;
};

export const isInListItem = (editor) => getListItemDepth(editor) !== null;

export const isListItemEmpty = (listItem) => {
  if (!listItem) {
    return false;
  }
  if (listItem.textContent.trim().length > 0) {
    return false;
  }
  let hasInlineNonText = false;
  listItem.descendants((node) => {
    if (node.isInline && node.type.name !== "text" && node.type.name !== "hardBreak") {
      hasInlineNonText = true;
      return false;
    }
    return true;
  });
  return !hasInlineNonText;
};

export const isDocEmptyParagraph = (doc) => {
  if (!doc) {
    return true;
  }
  if (doc.childCount === 0) {
    return true;
  }
  if (doc.childCount !== 1) {
    return false;
  }
  const child = doc.child(0);
  if (child.type.name !== "paragraph") {
    return false;
  }
  if (child.textContent.trim().length > 0) {
    return false;
  }
  let hasInlineNonText = false;
  child.descendants((node) => {
    if (node.isInline && node.type.name !== "text" && node.type.name !== "hardBreak") {
      hasInlineNonText = true;
      return false;
    }
    return true;
  });
  return !hasInlineNonText;
};

export const hasBulletList = (editor) => {
  if (!editor) {
    return false;
  }
  const doc = editor.state.doc;
  return doc.childCount > 0 && doc.child(0).type.name === "bulletList";
};

export const isOutermostListItem = (editor) => {
  if (!editor) {
    return false;
  }
  const { $from } = editor.state.selection;
  let listItemDepth = null;
  for (let depth = $from.depth; depth > 0; depth -= 1) {
    if ($from.node(depth).type.name === "listItem") {
      listItemDepth = depth;
      break;
    }
  }
  if (!listItemDepth) {
    return false;
  }
  const listDepth = listItemDepth - 1;
  const listNode = $from.node(listDepth);
  return listNode?.type?.name === "bulletList" && listDepth === 1;
};

export const blockNonEmptyListItemBackspace = (editor) => {
  if (!editor) {
    return false;
  }
  const { state } = editor;
  const { selection } = state;
  if (!selection.empty) {
    return false;
  }
  const { $from } = selection;
  const listItemDepth = getListItemDepth(editor);
  if (!listItemDepth) {
    return false;
  }
  const listItem = $from.node(listItemDepth);
  if (isListItemEmpty(listItem)) {
    return false;
  }
  if ($from.parentOffset !== 0) {
    return false;
  }
  return true;
};

export const handleEmptyListItemBackspace = (editor) => {
  if (!editor) {
    return false;
  }
  const { state, view } = editor;
  const { selection } = state;
  if (!selection.empty) {
    return false;
  }
  const { $from } = selection;
  let listItemDepth = null;
  for (let depth = $from.depth; depth > 0; depth -= 1) {
    if ($from.node(depth).type.name === "listItem") {
      listItemDepth = depth;
      break;
    }
  }
  if (!listItemDepth) {
    return false;
  }
  const listDepth = listItemDepth - 1;
  const listNode = $from.node(listDepth);
  if (!listNode || listNode.type.name !== "bulletList") {
    return false;
  }
  const listIndex = $from.index(listDepth);
  const listItem = $from.node(listItemDepth);
  if (!isListItemEmpty(listItem)) {
    return false;
  }

  if (listIndex <= 0) {
    const startPos = $from.start(listItemDepth) + 1;
    const trStart = state.tr.setSelection(TextSelection.create(state.doc, startPos));
    view.dispatch(trStart);
    editor.commands.focus();
    return true;
  }

  const listStart = $from.before(listDepth) + 1;
  let prevEnd = listStart + listNode.child(0).nodeSize - 2;
  let pos = listStart;
  for (let i = 0; i < listIndex; i += 1) {
    const child = listNode.child(i);
    if (i === listIndex - 1) {
      prevEnd = pos + child.nodeSize - 2;
      break;
    }
    pos += child.nodeSize;
  }

  const tr = state.tr.delete($from.before(listItemDepth), $from.after(listItemDepth));
  tr.setSelection(TextSelection.create(tr.doc, Math.max(prevEnd, 1)));
  view.dispatch(tr);
  editor.commands.focus();
  return true;
};

export const insertListItemAfterSelection = (editor) => {
  if (!editor) {
    return false;
  }
  const { state, view } = editor;
  const { $from } = state.selection;
  const listItemDepth = getListItemDepth(editor);
  if (!listItemDepth) {
    return false;
  }
  const listDepth = listItemDepth - 1;
  const listNode = $from.node(listDepth);
  if (!listNode || listNode.type.name !== "bulletList") {
    return false;
  }
  const listItemType = state.schema.nodes.listItem;
  const paragraphType = state.schema.nodes.paragraph;
  if (!listItemType || !paragraphType) {
    return false;
  }
  const newItem = listItemType.createAndFill(null, paragraphType.createAndFill());
  if (!newItem) {
    return false;
  }
  const insertPos = $from.after(listItemDepth);
  const tr = state.tr.insert(insertPos, newItem);
  const selectionPos = insertPos + 2;
  tr.setSelection(TextSelection.create(tr.doc, selectionPos));
  view.dispatch(tr);
  editor.commands.focus();
  return true;
};

export const appendListItem = (editor) => {
  if (!editor) {
    return false;
  }
  const { state, view } = editor;
  const { doc, schema } = state;
  const listNode = doc.childCount > 0 ? doc.child(0) : null;
  const listItemType = schema.nodes.listItem;
  const paragraphType = schema.nodes.paragraph;
  const listType = schema.nodes.bulletList;
  if (!listItemType || !paragraphType || !listType) {
    return false;
  }
  const newItem = listItemType.createAndFill(null, paragraphType.createAndFill());
  if (!newItem) {
    return false;
  }
  if (!listNode || listNode.type.name !== "bulletList") {
    const list = listType.create(null, newItem);
    const tr = state.tr.replaceWith(0, doc.content.size, list);
    const selectionPos = Math.min(tr.doc.content.size, 2);
    tr.setSelection(TextSelection.create(tr.doc, selectionPos));
    view.dispatch(tr);
    editor.commands.focus();
    return true;
  }
  const listPos = 1;
  const insertPos = listPos + listNode.nodeSize - 1;
  const tr = state.tr.insert(insertPos, newItem);
  const selectionPos = insertPos + 2;
  tr.setSelection(TextSelection.create(tr.doc, selectionPos));
  view.dispatch(tr);
  editor.commands.focus();
  return true;
};

const listItemToTitleDoc = (listItem) => {
  const paragraphs = [];
  if (listItem?.content) {
    for (const node of listItem.content) {
      if (node.type === "paragraph") {
        paragraphs.push(node);
      }
    }
  }
  const inlineContent = [];
  paragraphs.forEach((para, index) => {
    if (para.content) {
      inlineContent.push(...para.content);
    }
    if (index < paragraphs.length - 1) {
      inlineContent.push({ type: "hardBreak" });
    }
  });
  return {
    type: "doc",
    content: [
      {
        type: "paragraph",
        content: inlineContent.length ? inlineContent : []
      }
    ]
  };
};

export const splitAtSelection = (editor) => {
  if (!editor) {
    return null;
  }
  const { $from } = editor.state.selection;
  let listItemDepth = null;
  for (let depth = $from.depth; depth > 0; depth -= 1) {
    if ($from.node(depth).type.name === "listItem") {
      listItemDepth = depth;
      break;
    }
  }
  if (!listItemDepth) {
    return null;
  }
  const listDepth = listItemDepth - 1;
  const listNode = $from.node(listDepth);
  if (!listNode || listNode.type.name !== "bulletList" || listDepth !== 1) {
    return null;
  }
  const listIndex = $from.index(listDepth);
  const doc = editor.getJSON();
  const listContent = doc.content?.[0]?.content ?? [];
  if (listIndex < 0 || listIndex >= listContent.length) {
    return null;
  }
  const before = listContent.slice(0, listIndex);
  const current = listContent[listIndex];
  const after = listContent.slice(listIndex + 1);
  const titleDoc = listItemToTitleDoc(current);
  const remaining = {
    type: "doc",
    content: [
      {
        type: "bulletList",
        content: before.length
          ? before
          : [
              {
                type: "listItem",
                content: [{ type: "paragraph" }]
              }
            ]
      }
    ]
  };
  const newTaskContent = {
    type: "doc",
    content: [
      {
        type: "bulletList",
        content: after.length
          ? after
          : [
              {
                type: "listItem",
                content: [{ type: "paragraph" }]
              }
            ]
      }
    ]
  };
  return { remainingContent: remaining, newTaskContent, title: titleDoc };
};
