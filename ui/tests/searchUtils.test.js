import assert from "node:assert/strict";
import { highlightDoc, tokenizeQuery } from "../src/utils/searchUtils.js";

const docWithText = (text) => ({
  type: "doc",
  content: [
    {
      type: "paragraph",
      content: [{ type: "text", text }]
    }
  ]
});

assert.deepEqual(tokenizeQuery("new  task"), ["new", "task"]);

const doc = docWithText("new");
const result = highlightDoc(doc, ["ew"]);
const paragraph = result.content[0];
const textNode = paragraph.content.find((node) => node.marks);
assert.ok(textNode);
assert.equal(textNode.marks[0].type, "highlight");
assert.equal(textNode.marks[0].attrs.color, "search");
