import { highlightDoc } from "./searchUtils.js";

export const emptyTitleDoc = () => ({
  type: "doc",
  content: [{ type: "paragraph" }]
});

export const emptyContentDoc = () => ({
  type: "doc",
  content: [{ type: "paragraph" }]
});

const titleFromText = (text) => ({
  type: "doc",
  content: [
    {
      type: "paragraph",
      content: text ? [{ type: "text", text }] : []
    }
  ]
});

export const normalizeContent = (content) => {
  if (!content || content.type !== "doc") {
    return emptyContentDoc();
  }
  const nodes = content.content || [];
  if (nodes.length === 0) {
    return emptyContentDoc();
  }
  const list = nodes[0];
  if (list && list.type === "bulletList") {
    if (!list.content || list.content.length === 0) {
      return emptyContentDoc();
    }
    return content;
  }
  if (
    nodes.length === 1
    && nodes[0].type === "paragraph"
    && (!nodes[0].content || nodes[0].content.length === 0)
  ) {
    return emptyContentDoc();
  }
  const paragraphs = nodes.filter((node) => node.type === "paragraph");
  if (paragraphs.length > 0) {
    return {
      type: "doc",
      content: [
        {
          type: "bulletList",
          content: paragraphs.map((paragraph) => ({
            type: "listItem",
            content: [paragraph]
          }))
        }
      ]
    };
  }
  return emptyContentDoc();
};

export const normalizeTitle = (title) => {
  if (typeof title === "string") {
    return titleFromText(title);
  }
  if (!title || title.type !== "doc") {
    return emptyTitleDoc();
  }
  if (!title.content || title.content.length === 0) {
    return emptyTitleDoc();
  }
  return title;
};

export const normalizeTask = (task) => ({
  ...task,
  title: normalizeTitle(task.title),
  content: normalizeContent(task.content)
});

export const highlightTask = (task, terms) => ({
  ...task,
  title: highlightDoc(task.title, terms),
  content: highlightDoc(task.content, terms)
});

export const titleDocToListItem = (titleDoc) => {
  const paragraphs = [];
  if (titleDoc?.content) {
    for (const node of titleDoc.content) {
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
    type: "listItem",
    content: [
      {
        type: "paragraph",
        content: inlineContent.length ? inlineContent : []
      }
    ]
  };
};
