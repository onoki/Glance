import assert from "node:assert/strict";
import { hasStatusMarker, toggleTitleStatusInDoc, toggleWholeTaskStatus } from "../src/utils/statusMetadata.js";

const original = { type: "doc", content: [{ type: "paragraph", content: [{ type: "text", text: "Risk" }] }] };
const marked = toggleTitleStatusInDoc(original);
assert.equal(original.content[0].attrs, undefined);
assert.ok(marked.content[0].attrs.glanceId);
assert.ok(marked.content[0].attrs.statusInputAtUtc);
assert.equal(hasStatusMarker(original), false);
assert.equal(hasStatusMarker(marked), true);
const cleared = toggleTitleStatusInDoc(marked);
assert.equal(cleared.content[0].attrs.statusInputAtUtc, null);
assert.equal(cleared.content[0].attrs.glanceId, marked.content[0].attrs.glanceId);

const contentMarked = { type: "doc", content: [{ type: "bulletList", content: [{ type: "listItem", attrs: { glanceId: "line", statusInputAtUtc: "2026-08-01T00:00:00Z" }, content: [{ type: "paragraph" }] }] }] };
const wholeCleared = toggleWholeTaskStatus(original, contentMarked);
assert.equal(wholeCleared.content.content[0].content[0].attrs.statusInputAtUtc, null);
