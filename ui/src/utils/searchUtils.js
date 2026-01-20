export const escapeRegex = (value) => value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

export const tokenizeQuery = (query) =>
  query
    .split(/\s+/)
    .map((token) => token.trim())
    .filter((token) => token.length > 0);

const buildSearchRegex = (terms) => {
  const unique = Array.from(new Set(terms));
  if (unique.length === 0) {
    return null;
  }
  const pattern = unique.map(escapeRegex).join("|");
  return new RegExp(pattern, "gi");
};

const addSearchMark = (marks) => {
  const next = marks ? [...marks] : [];
  if (next.some((mark) => mark.type === "highlight" && mark.attrs?.color === "search")) {
    return next;
  }
  next.push({ type: "highlight", attrs: { color: "search" } });
  return next;
};

const highlightTextNode = (node, regex) => {
  if (!node.text || !regex) {
    return [node];
  }
  const text = node.text;
  const matches = [...text.matchAll(regex)];
  if (matches.length === 0) {
    return [node];
  }
  const parts = [];
  let lastIndex = 0;
  for (const match of matches) {
    const index = match.index ?? 0;
    const value = match[0] ?? "";
    if (index > lastIndex) {
      parts.push({ ...node, text: text.slice(lastIndex, index), marks: node.marks });
    }
    parts.push({ ...node, text: value, marks: addSearchMark(node.marks) });
    lastIndex = index + value.length;
  }
  if (lastIndex < text.length) {
    parts.push({ ...node, text: text.slice(lastIndex), marks: node.marks });
  }
  return parts;
};

const highlightNode = (node, regex) => {
  if (!node || typeof node !== "object") {
    return node;
  }
  if (node.type === "text") {
    return highlightTextNode(node, new RegExp(regex.source, regex.flags));
  }
  if (!node.content) {
    return { ...node };
  }
  const nextContent = [];
  for (const child of node.content) {
    const transformed = highlightNode(child, regex);
    if (Array.isArray(transformed)) {
      nextContent.push(...transformed);
    } else {
      nextContent.push(transformed);
    }
  }
  return { ...node, content: nextContent };
};

export const highlightDoc = (doc, terms) => {
  const regex = buildSearchRegex(terms);
  if (!regex || !doc) {
    return doc;
  }
  return highlightNode(doc, regex);
};
