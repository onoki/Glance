import assert from "node:assert/strict";
import { isDocEmptyJson, isListItemEmpty } from "../src/utils/taskDocUtils.js";

const emptyDoc = { type: "doc", content: [{ type: "paragraph" }] };
const textDoc = { type: "doc", content: [{ type: "paragraph", content: [{ type: "text", text: "hi" }] }] };
const listDoc = {
  type: "doc",
  content: [
    {
      type: "bulletList",
      content: [{ type: "listItem", content: [{ type: "paragraph" }] }]
    }
  ]
};

assert.equal(isDocEmptyJson(null), true);
assert.equal(isDocEmptyJson(emptyDoc), true);
assert.equal(isDocEmptyJson(listDoc), true);
assert.equal(isDocEmptyJson(textDoc), false);

const emptyListItem = {
  textContent: "",
  descendants: () => {}
};

const textListItem = {
  textContent: "hello",
  descendants: () => {}
};

const inlineListItem = {
  textContent: "",
  descendants: (cb) => {
    cb({ isInline: true, type: { name: "image" } });
  }
};

assert.equal(isListItemEmpty(emptyListItem), true);
assert.equal(isListItemEmpty(textListItem), false);
assert.equal(isListItemEmpty(inlineListItem), false);
