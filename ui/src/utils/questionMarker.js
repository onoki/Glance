export const toggleQuestionInParagraphs = (state, ranges) => {
  const tr = state.tr;
  for (const { from, node } of [...ranges].reverse()) {
    const text = node.textContent;
    const prefixLength = text.match(/^(?:[☐☑] ?)?(?:[⭐★] ?)?/)[0].length;
    const position = from + prefixLength;
    if (text.slice(prefixLength).startsWith("❓")) {
      tr.delete(position, position + (text[prefixLength + 1] === " " ? 2 : 1));
    } else tr.insert(position, state.schema.text("❓ "));
  }
  return tr;
};
