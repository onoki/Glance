import assert from "node:assert/strict";
import { Schema } from "prosemirror-model";
import { EditorState } from "prosemirror-state";
import { toggleQuestionInParagraphs } from "../src/utils/questionMarker.js";

const schema = new Schema({ nodes: { doc: { content: "paragraph+" }, paragraph: { content: "text*" }, text: {} } });
const paragraphs = ["Title", "☐ ⭐ Subtask", "★ Legacy star", ""];
let state = EditorState.create({ schema, doc: schema.node("doc", null, paragraphs.map(text => schema.node("paragraph", null, text ? [schema.text(text)] : []))) });
const toggle = () => {
  const ranges = [];
  state.doc.forEach((node, offset) => ranges.push({ from: offset + 1, node }));
  state = state.apply(toggleQuestionInParagraphs(state, ranges));
};
toggle();
assert.deepEqual(Array.from({ length: 4 }, (_, i) => state.doc.child(i).textContent), ["❓ Title", "☐ ⭐ ❓ Subtask", "★ ❓ Legacy star", "❓ "]);
assert.equal(schema.nodeFromJSON(state.doc.toJSON()).textContent, state.doc.textContent, "question markers survive serialization");
toggle();
assert.deepEqual(Array.from({ length: 4 }, (_, i) => state.doc.child(i).textContent), paragraphs);
console.log("Question markers toggle on titles and multiple lines");
