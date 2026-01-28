import { TextSelection } from "prosemirror-state";
import { isDocEmptyJson } from "./taskDocUtils.js";

const LIST_TYPES = new Set(["bulletList", "taskList"]);
const LIST_ITEM_TYPES = new Set(["listItem", "taskItem"]);

const isListTypeName = (name) => LIST_TYPES.has(name);
const isListItemTypeName = (name) => LIST_ITEM_TYPES.has(name);

export const getDefaultListTypeName = (schema) =>
  schema?.nodes?.taskList ? "taskList" : "bulletList";

export const getDefaultListItemTypeName = (schema) =>
  schema?.nodes?.taskItem ? "taskItem" : "listItem";

export const getListItemTypeNameForListType = (listTypeName) =>
  listTypeName === "taskList" ? "taskItem" : "listItem";

export const getListTypeNameForItemType = (listItemTypeName) =>
  listItemTypeName === "taskItem" ? "taskList" : "bulletList";

export const isListNodeName = (name) => isListTypeName(name);
export const isListItemNodeName = (name) => isListItemTypeName(name);

const buildListItem = (schema, itemTypeName) => {
  const listItemType = schema.nodes[itemTypeName];
  const paragraphType = schema.nodes.paragraph;
  if (!listItemType || !paragraphType) {
    return null;
  }
  const attrs = itemTypeName === "taskItem" ? { checked: false } : null;
  const paragraph = paragraphType.createAndFill();
  if (!paragraph) {
    return null;
  }
  return listItemType.createAndFill(attrs, paragraph);
};

export const getListItemDepth = (editor) => {
  if (!editor) {
    return null;
  }
  const { $from } = editor.state.selection;
  for (let depth = $from.depth; depth > 0; depth -= 1) {
    if (isListItemTypeName($from.node(depth).type.name)) {
      return depth;
    }
  }
  return null;
};

export const isInListItem = (editor) => getListItemDepth(editor) !== null;

export const getActiveListItemType = (editor) => {
  if (!editor) {
    return null;
  }
  const listItemDepth = getListItemDepth(editor);
  if (!listItemDepth) {
    return null;
  }
  return editor.state.selection.$from.node(listItemDepth).type.name;
};

export const getActiveListType = (editor) => {
  if (!editor) {
    return null;
  }
  const listItemDepth = getListItemDepth(editor);
  if (!listItemDepth) {
    return null;
  }
  const listDepth = listItemDepth - 1;
  const listNode = editor.state.selection.$from.node(listDepth);
  if (!listNode || !isListTypeName(listNode.type.name)) {
    return null;
  }
  return listNode.type.name;
};

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

export const hasList = (editor) => {
  if (!editor) {
    return false;
  }
  const doc = editor.state.doc;
  return doc.childCount > 0 && isListTypeName(doc.child(0).type.name);
};

export const isOutermostListItem = (editor) => {
  if (!editor) {
    return false;
  }
  const { $from } = editor.state.selection;
  let listItemDepth = null;
  for (let depth = $from.depth; depth > 0; depth -= 1) {
    if (isListItemTypeName($from.node(depth).type.name)) {
      listItemDepth = depth;
      break;
    }
  }
  if (!listItemDepth) {
    return false;
  }
  const listDepth = listItemDepth - 1;
  const listNode = $from.node(listDepth);
  return isListTypeName(listNode?.type?.name) && listDepth === 1;
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
    if (isListItemTypeName($from.node(depth).type.name)) {
      listItemDepth = depth;
      break;
    }
  }
  if (!listItemDepth) {
    return false;
  }
  const listDepth = listItemDepth - 1;
  const listNode = $from.node(listDepth);
  if (!listNode || !isListTypeName(listNode.type.name)) {
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
  if (!listNode || !isListTypeName(listNode.type.name)) {
    return false;
  }
  const listItemTypeName = getListItemTypeNameForListType(listNode.type.name);
  const newItem = buildListItem(state.schema, listItemTypeName);
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
  const listTypeName = listNode && isListTypeName(listNode.type.name)
    ? listNode.type.name
    : getDefaultListTypeName(schema);
  const listType = schema.nodes[listTypeName];
  if (!listType) {
    return false;
  }
  const listItemTypeName = getListItemTypeNameForListType(listTypeName);
  const newItem = buildListItem(schema, listItemTypeName);
  if (!newItem) {
    return false;
  }
  if (!listNode || !isListTypeName(listNode.type.name)) {
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
    if (isListItemTypeName($from.node(depth).type.name)) {
      listItemDepth = depth;
      break;
    }
  }
  if (!listItemDepth) {
    return null;
  }
  const listDepth = listItemDepth - 1;
  const listNode = $from.node(listDepth);
  if (!listNode || !isListTypeName(listNode.type.name) || listDepth !== 1) {
    return null;
  }
  const listTypeName = listNode.type.name;
  const listItemTypeName = getListItemTypeNameForListType(listTypeName);
  const listIndex = $from.index(listDepth);
  const doc = editor.getJSON();
  const listContent = doc.content?.[0]?.content ?? [];
  if (listIndex < 0 || listIndex >= listContent.length) {
    return null;
  }
  const removeEmptyItems = (items) => items.filter((item) => !isDocEmptyJson(item));
  const before = removeEmptyItems(listContent.slice(0, listIndex));
  const current = listContent[listIndex];
  const after = removeEmptyItems(listContent.slice(listIndex + 1));
  const titleDoc = listItemToTitleDoc(current);
  const emptyItem = () => {
    const item = {
      type: listItemTypeName,
      content: [{ type: "paragraph" }]
    };
    if (listItemTypeName === "taskItem") {
      item.attrs = { checked: false };
    }
    return item;
  };

  const emptyDoc = {
    type: "doc",
    content: [{ type: "paragraph" }]
  };
  const remaining = before.length
    ? {
      type: "doc",
      content: [
        {
          type: listTypeName,
          content: before
        }
      ]
    }
    : emptyDoc;
  const newTaskContent = after.length
    ? {
      type: "doc",
      content: [
        {
          type: listTypeName,
          content: after
        }
      ]
    }
    : emptyDoc;
  return { remainingContent: remaining, newTaskContent, title: titleDoc };
};
