import assert from "node:assert/strict";
import { formatDateKey, getWeekStart, parseDateKey } from "../src/utils/dateUtils.js";
import { Schema } from "prosemirror-model";
import { EditorState, TextSelection } from "prosemirror-state";
import { history, undo } from "prosemirror-history";
import { insertDateShortcut } from "../src/utils/insertDateShortcut.js";

const value = formatDateKey(new Date("2024-03-05T10:00:00Z"));
assert.equal(value, "2024-03-05");

const date = parseDateKey("2024-03-05");
assert.ok(date instanceof Date);
assert.equal(date?.getFullYear(), 2024);

const weekStart = getWeekStart(new Date("2024-03-07T12:00:00Z"));
assert.equal(weekStart.getDay(), 1);

const schema = new Schema({ nodes: {
  doc: { content: "block+" }, paragraph: { content: "text*", group: "block" },
  bulletList: { content: "listItem+", group: "block" },
  listItem: { content: "paragraph+" }, text: {}
} });
for (const subcontent of [false, true]) {
  for (const replaceSelection of [false, true]) {
    const paragraph = schema.node("paragraph", null, schema.text("before after"));
    const doc = schema.node("doc", null, subcontent
      ? schema.node("bulletList", null, schema.node("listItem", null, paragraph)) : paragraph);
    const from = (subcontent ? 3 : 1) + 7;
    const view = { state: EditorState.create({ doc, plugins: [history()],
      selection: TextSelection.create(doc, from, replaceSelection ? from + 5 : from) }) };
    view.dispatch = (tr) => { view.state = view.state.apply(tr); };
    let prevented = false;
    const event = { key: "D", altKey: true, shiftKey: true, preventDefault() { prevented = true; } };
    const localMidnight = () => new Date(2026, 8, 8, 0, 1);
    assert.equal(insertDateShortcut(view, event, true, localMidnight), true);
    assert.equal(prevented, true);
    assert.equal(view.state.doc.textContent, replaceSelection ? "before 2026-09-08" : "before 2026-09-08after");
    assert.equal(view.state.selection.from, from + 10);
    assert.equal(view.state.selection.empty, true);
    assert.equal(undo(view.state, view.dispatch), true);
    assert.equal(view.state.doc.textContent, "before after");
    for (const [editable, modifiers] of [[false, {}], [true, { shiftKey: false }],
      [true, { altKey: false }], [true, { ctrlKey: true }], [true, { isComposing: true }]]) {
      prevented = false;
      assert.equal(insertDateShortcut(view, { ...event, ...modifiers }, editable, localMidnight), false);
      assert.equal(prevented, false);
      assert.equal(view.state.doc.textContent, "before after");
    }
  }
}
console.log("Local date shortcut: titles, subcontent, selections, caret, undo and read-only protection passed");
