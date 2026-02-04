import { emptyTitleDoc } from "./taskUtils.js";

const cloneMarks = (marks) => (marks ? marks.map((mark) => ({ ...mark })) : undefined);

const textNode = (text, marks) => {
  const node = { type: "text", text };
  if (marks && marks.length) {
    node.marks = cloneMarks(marks);
  }
  return node;
};

const inlineLength = (node) => {
  if (!node) {
    return 0;
  }
  if (node.type === "text") {
    return node.text ? node.text.length : 0;
  }
  return 1;
};

const splitInlineContent = (nodes, fromOffset, toOffset) => {
  const before = [];
  const after = [];
  let cursor = 0;
  const from = Math.max(0, fromOffset);
  const to = Math.max(from, toOffset);

  nodes.forEach((node) => {
    const length = inlineLength(node);
    const nextCursor = cursor + length;

    if (nextCursor <= from) {
      before.push(node);
    } else if (cursor >= to) {
      after.push(node);
    } else if (node.type === "text") {
      const startInNode = Math.max(0, from - cursor);
      const endInNode = Math.min(length, to - cursor);
      const text = node.text || "";
      if (startInNode > 0) {
        before.push(textNode(text.slice(0, startInNode), node.marks));
      }
      if (endInNode < length) {
        after.push(textNode(text.slice(endInNode), node.marks));
      }
    } else {
      if (cursor < from) {
        before.push(node);
      } else if (cursor >= to) {
        after.push(node);
      }
    }

    cursor = nextCursor;
  });

  return { before, after };
};

const buildDoc = (inline) => ({
  type: "doc",
  content: [
    {
      type: "paragraph",
      content: inline.length ? inline : []
    }
  ]
});

export const splitTitleDocAtOffsets = (titleDoc, fromOffset, toOffset) => {
  if (!titleDoc || titleDoc.type !== "doc") {
    return { before: emptyTitleDoc(), after: emptyTitleDoc() };
  }
  const paragraph = Array.isArray(titleDoc.content)
    ? titleDoc.content.find((node) => node.type === "paragraph")
    : null;
  const content = Array.isArray(paragraph?.content) ? paragraph.content : [];
  const { before, after } = splitInlineContent(content, fromOffset, toOffset);
  return {
    before: buildDoc(before),
    after: buildDoc(after)
  };
};
