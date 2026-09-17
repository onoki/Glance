import assert from "node:assert/strict";
import { Schema } from "prosemirror-model";
import { EditorState, TextSelection } from "prosemirror-state";
import { compactUrlLabel, insertOutsideLink } from "../src/utils/editorLinks.js";

const href = `https://example.com/${"long-path".repeat(20)}?secret=retained`;
assert.equal(compactUrlLabel(href, href), "https://example.com/…");
assert.equal(compactUrlLabel("Custom link label", href), null);
assert.equal(compactUrlLabel("https://example.com", "https://example.com"), null);
assert.equal(compactUrlLabel("file:///" + "a".repeat(90), "file:///" + "a".repeat(90)), null);
const schema = new Schema({
  nodes: { doc: { content: "paragraph+" }, paragraph: { content: "inline*" }, text: { group: "inline" } },
  marks: { link: { attrs: { href: {} }, inclusive: false }, strong: {} }
});
const link = schema.marks.link.create({ href });
const makeView = (position) => {
  const doc = schema.node("doc", null, [schema.node("paragraph", null, [schema.text(href, [link, schema.marks.strong.create()])])]);
  const view = { state: EditorState.create({ schema, doc, selection: TextSelection.create(doc, position) }) };
  view.dispatch = (tr) => { view.state = view.state.apply(tr); };
  return view;
};
for (const position of [1, href.length + 1]) {
  const view = makeView(position);
  assert.equal(insertOutsideLink(view, position, position, "plain "), true);
  const inserted = view.state.doc.nodeAt(position);
  assert.equal(inserted.marks.some((mark) => mark.type.name === "link"), false);
  const links = [];
  view.state.doc.descendants((node) => { if (node.isText && node.marks.some((mark) => mark.type.name === "link")) links.push(node); });
  assert.equal(links.map((node) => node.text).join(""), href);
  assert.equal(links[0].marks.find((mark) => mark.type.name === "link").attrs.href, href);
}
assert.equal(insertOutsideLink(makeView(10), 10, 10, "inside"), false, "editing within a link remains possible");
assert.equal(insertOutsideLink(makeView(1), 1, 5, "replacement"), false);
console.log("Link boundary and compact label tests passed");
