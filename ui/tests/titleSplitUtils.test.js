import assert from "node:assert/strict";
import { splitTitleDocAtOffsets } from "../src/utils/titleSplitUtils.js";
import { mergeTitleDocs } from "../src/utils/taskUtils.js";

const buildDoc = (nodes) => ({
  type: "doc",
  content: [
    {
      type: "paragraph",
      content: nodes
    }
  ]
});

const textNode = (text, marks = null) => {
  const node = { type: "text", text };
  if (marks) {
    node.marks = marks;
  }
  return node;
};

const highlight = (color) => [{ type: "highlight", attrs: { color } }];

const textContent = (doc) =>
  (doc?.content?.[0]?.content || [])
    .map((node) => node.text || (node.type === "hardBreak" ? "\n" : ""))
    .join("");

{
  const doc = buildDoc([textNode("HelloWorld")]);
  const { before, after } = splitTitleDocAtOffsets(doc, 5, 5);
  assert.equal(textContent(before), "Hello");
  assert.equal(textContent(after), "World");
}

{
  const doc = buildDoc([textNode("HelloWorld")]);
  const { before, after } = splitTitleDocAtOffsets(doc, 2, 4);
  assert.equal(textContent(before), "He");
  assert.equal(textContent(after), "oWorld");
}

{
  const doc = buildDoc([
    textNode("Hello", highlight("green")),
    { type: "hardBreak" },
    textNode("World", highlight("green"))
  ]);
  const { before, after } = splitTitleDocAtOffsets(doc, 5, 5);
  assert.equal(textContent(before), "Hello");
  assert.equal(textContent(after), "\nWorld");
  assert.deepEqual(before.content[0].content[0].marks, highlight("green"));
  assert.deepEqual(after.content[0].content[1].marks, highlight("green"));
}

{
  const first = buildDoc([textNode("Hello")]);
  const second = buildDoc([textNode("World")]);
  const merged = mergeTitleDocs(first, second);
  assert.equal(textContent(merged), "HelloWorld");
}
