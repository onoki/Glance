export const isDocEmptyJson = (node) => {
  if (!node) {
    return true;
  }
  if (node.type === "text") {
    return !(node.text || "").trim();
  }
  if (node.type === "hardBreak") {
    return true;
  }
  if (node.content && Array.isArray(node.content)) {
    return node.content.every((child) => isDocEmptyJson(child));
  }
  const containerTypes = new Set(["doc", "paragraph", "bulletList", "listItem", "taskList", "taskItem"]);
  if (node.type && containerTypes.has(node.type)) {
    return true;
  }
  return false;
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

export const isSingleEmptyBullet = (doc) => {
  const [list] = doc?.content || [];
  const [item] = list?.content || [];
  const [paragraph] = item?.content || [];
  return doc?.content?.length === 1 && list?.type === "bulletList" && list.content?.length === 1
    && item?.type === "listItem" && item.content?.length === 1
    && paragraph?.type === "paragraph" && !paragraph.content?.length;
};
