import { highlightDoc } from "./searchUtils.js";

export const emptyTitleDoc = () => ({
  type: "doc",
  content: [{ type: "paragraph" }]
});

export const emptyContentDoc = () => ({
  type: "doc",
  content: [{ type: "paragraph" }]
});

const SUBCONTENT_LIST_TYPES = new Set(["bulletList", "taskList"]);
const CHECKBOX_EMPTY = "☐";
const CHECKBOX_CHECKED = "☑";

const ensureCheckboxPrefix = (paragraph, checked) => {
  const prefix = checked ? `${CHECKBOX_CHECKED} ` : `${CHECKBOX_EMPTY} `;
  const content = paragraph.content ? [...paragraph.content] : [];
  if (content.length > 0 && content[0].type === "text") {
    const text = content[0].text || "";
    if (text.startsWith(CHECKBOX_EMPTY) || text.startsWith(CHECKBOX_CHECKED)) {
      const rest = text.slice(1);
      const restText = rest.startsWith(" ") ? rest.slice(1) : rest;
      content[0] = { ...content[0], text: `${prefix}${restText}` };
      return { ...paragraph, content };
    }
  }
  return { ...paragraph, content: [{ type: "text", text: prefix }, ...content] };
};

const taskItemToListItem = (item) => {
  const checked = item?.attrs?.checked ?? false;
  const rawContent = Array.isArray(item?.content) ? item.content : [];
  const content = rawContent.length
    ? rawContent.map((node, index) => {
        if (index === 0 && node.type === "paragraph") {
          return ensureCheckboxPrefix(node, checked);
        }
        return node;
      })
    : [ensureCheckboxPrefix({ type: "paragraph", content: [] }, checked)];
  return { type: "listItem", content };
};

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
  if (list && SUBCONTENT_LIST_TYPES.has(list.type)) {
    if (!list.content || list.content.length === 0) {
      return emptyContentDoc();
    }
    if (list.type === "taskList") {
      const converted = list.content.map(taskItemToListItem);
      if (!converted.length) {
        return emptyContentDoc();
      }
      return {
        type: "doc",
        content: [
          {
            type: "bulletList",
            content: converted
          }
        ]
      };
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
