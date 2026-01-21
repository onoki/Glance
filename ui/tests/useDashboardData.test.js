import assert from "node:assert/strict";
import { shouldDeleteEmptyOnComplete } from "../src/utils/taskCompletionUtils.js";

const emptyDoc = { type: "doc", content: [{ type: "paragraph" }] };
const textDoc = { type: "doc", content: [{ type: "paragraph", content: [{ type: "text", text: "hi" }] }] };

assert.equal(
  shouldDeleteEmptyOnComplete({ completedAt: null, title: emptyDoc, content: emptyDoc }),
  true
);
assert.equal(
  shouldDeleteEmptyOnComplete({ completedAt: Date.now(), title: emptyDoc, content: emptyDoc }),
  false
);
assert.equal(
  shouldDeleteEmptyOnComplete({ completedAt: null, title: textDoc, content: emptyDoc }),
  false
);
assert.equal(
  shouldDeleteEmptyOnComplete({ completedAt: null, title: emptyDoc, content: textDoc }),
  false
);
