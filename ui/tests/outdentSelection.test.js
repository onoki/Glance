import assert from 'node:assert/strict';
import { Schema } from 'prosemirror-model';
import { EditorState, TextSelection } from 'prosemirror-state';
import { splitAtSelection } from '../src/utils/editorListUtils.js';

const schema = new Schema({
  nodes: {
    doc: { content: 'block+' }, paragraph: { group: 'block', content: 'inline*' },
    bulletList: { group: 'block', content: 'listItem+' }, listItem: { content: 'paragraph block*' },
    text: { group: 'inline' }, hardBreak: { group: 'inline', inline: true },
    image: { group: 'inline', inline: true, atom: true }
  },
  marks: { bold: {}, link: { attrs: { href: {} } } }
});
const paragraph = (content) => schema.node('paragraph', null, content);
const item = (content) => schema.node('listItem', null, content);
const split = (paragraphs, paragraphIndex, offset, endOffset = offset) => {
  const doc = schema.node('doc', null, [schema.node('bulletList', null, [
    item([paragraph([schema.text('Previous')])]), item(paragraphs), item([paragraph([schema.text('Following')])])
  ])]);
  let start;
  let index = -1;
  doc.descendants((node, pos) => {
    if (node.type.name === 'paragraph') {
      index += 1;
      if (index === paragraphIndex + 1) start = pos + 1;
    }
  });
  const state = EditorState.create({ schema, doc, selection: TextSelection.create(doc, start + offset, start + endOffset) });
  const result = splitAtSelection({ state, getJSON: () => doc.toJSON() });
  assert.equal(result.remainingContent.content[0].content.length, 1);
  assert.match(JSON.stringify(result.newTaskContent), /Following/);
  return result;
};

for (const offset of [0, 5, 11]) {
  assert.deepEqual(split([paragraph([schema.text('Hello world')])], 0, offset).titleSelection, { from: offset + 1, to: offset + 1 });
}
const rich = paragraph([
  schema.text('😀 ', [schema.marks.bold.create()]), schema.node('image'), schema.node('hardBreak'),
  schema.text('linked text', [schema.marks.link.create({ href: 'https://example.com' })])
]);
assert.deepEqual(split([rich], 0, 8).titleSelection, { from: 9, to: 9 });
assert.deepEqual(split([paragraph([schema.text('First')]), rich], 1, 8).titleSelection, { from: 15, to: 15 });
assert.deepEqual(split([rich], 0, 6, 10).titleSelection, { from: 7, to: 11 });
assert.deepEqual(split([paragraph([])], 0, 0).titleSelection, { from: 1, to: 1 });
console.log('Outdent preserves caret and selection across rich inline content and paragraphs');

const nestedDoc = schema.node('doc', null, [schema.node('bulletList', null, [
  item([paragraph([schema.text('Parent')]), schema.node('bulletList', null, [item([paragraph([schema.text('Nested child')])])])]),
  item([paragraph([schema.text('Next sibling')])])
])]);
const nestedState = EditorState.create({ schema, doc: nestedDoc, selection: TextSelection.create(nestedDoc, 5) });
const nestedSplit = splitAtSelection({ state: nestedState, getJSON: () => nestedDoc.toJSON() });
assert.match(JSON.stringify(nestedSplit.newTaskContent), /Nested child/);
assert.match(JSON.stringify(nestedSplit.newTaskContent), /Next sibling/);

const { sinkListItem } = await import('prosemirror-schema-list');
const multiline = schema.node('doc', null, [schema.node('bulletList', null, [
  item([paragraph([schema.text('First line')])]),
  item([paragraph([schema.text('Second'), schema.node('hardBreak'), schema.text('Continuation')])]),
  item([paragraph([schema.text('Third line')])])
])]);
for (const first of [true, false]) {
  const pos = first ? 4 : multiline.child(0).child(0).nodeSize + 4;
  let state = EditorState.create({ schema, doc: multiline, selection: TextSelection.create(multiline, pos) });
  const changed = sinkListItem(schema.nodes.listItem)(state, tr => { state = state.apply(tr); });
  assert.equal(changed, !first);
  assert.equal(state.doc.textContent, multiline.textContent, 'indent never removes later lines');
}
