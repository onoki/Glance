import { Extension } from "@tiptap/core";

const renderMetadata = (attributes) => {
  const html = {};
  if (attributes.glanceId) html["data-glance-id"] = attributes.glanceId;
  if (attributes.statusInputAtUtc) html["data-status-input-at-utc"] = attributes.statusInputAtUtc;
  return html;
};

export const StatusMetadata = Extension.create({
  name: "statusMetadata",
  addGlobalAttributes() {
    return [{
      types: ["paragraph", "listItem"],
      attributes: {
        glanceId: {
          default: null,
          parseHTML: (element) => element.getAttribute("data-glance-id"),
          renderHTML: renderMetadata
        },
        statusInputAtUtc: {
          default: null,
          parseHTML: (element) => element.getAttribute("data-status-input-at-utc"),
          renderHTML: renderMetadata
        }
      }
    }];
  }
});

const newId = () => globalThis.crypto?.randomUUID?.()
  ?? `glance-${Date.now()}-${Math.random().toString(16).slice(2)}`;

export const toggleStatusAtSelection = (editor, titleMode = false) => {
  if (!editor) return false;
  const { state, view } = editor;
  const targets = new Map();
  if (titleMode) {
    state.doc.descendants((node, pos) => {
      if (node.type.name === "paragraph" && targets.size === 0) targets.set(pos, node);
    });
  } else if (state.selection.empty) {
    const { $from } = state.selection;
    for (let depth = $from.depth; depth > 0; depth -= 1) {
      if ($from.node(depth).type.name === "listItem") {
        targets.set($from.before(depth), $from.node(depth));
        break;
      }
    }
  } else {
    state.doc.nodesBetween(state.selection.from, state.selection.to, (node, pos) => {
      if (node.type.name === "listItem") targets.set(pos, node);
    });
  }
  if (targets.size === 0) return false;
  const clear = [...targets.values()].every((node) => !!node.attrs.statusInputAtUtc);
  const timestamp = new Date().toISOString();
  let transaction = state.tr;
  for (const [position, node] of targets) {
    transaction = transaction.setNodeMarkup(position, undefined, {
      ...node.attrs,
      glanceId: node.attrs.glanceId || newId(),
      statusInputAtUtc: clear ? null : (node.attrs.statusInputAtUtc || timestamp)
    });
  }
  view.dispatch(transaction);
  editor.commands.focus();
  return true;
};

export const toggleTitleStatusInDoc = (document) => {
  const copy = structuredClone(document);
  const paragraph = copy?.content?.find((node) => node.type === "paragraph");
  if (!paragraph) return copy;
  paragraph.attrs = { ...(paragraph.attrs || {}) };
  paragraph.attrs.glanceId ||= newId();
  paragraph.attrs.statusInputAtUtc = paragraph.attrs.statusInputAtUtc ? null : new Date().toISOString();
  return copy;
};

export const hasStatusMarker = (node) => {
  if (!node || typeof node !== "object") return false;
  if (node.attrs?.statusInputAtUtc) return true;
  return Array.isArray(node.content) && node.content.some(hasStatusMarker);
};

const clearStatusMarkers = (node) => {
  if (!node || typeof node !== "object") return;
  if (node.attrs?.statusInputAtUtc) node.attrs.statusInputAtUtc = null;
  if (Array.isArray(node.content)) node.content.forEach(clearStatusMarkers);
};

export const toggleWholeTaskStatus = (title, content) => {
  const nextTitle = structuredClone(title);
  const nextContent = structuredClone(content);
  if (hasStatusMarker(nextTitle) || hasStatusMarker(nextContent)) {
    clearStatusMarkers(nextTitle);
    clearStatusMarkers(nextContent);
  } else {
    return { title: toggleTitleStatusInDoc(nextTitle), content: nextContent };
  }
  return { title: nextTitle, content: nextContent };
};
