import assert from 'node:assert/strict';
import { Schema } from 'prosemirror-model';
import { EditorState, TextSelection } from 'prosemirror-state';
import { splitAtSelection } from '../src/utils/editorListUtils.js';
import { indentTaskDocuments } from '../src/utils/taskJoin.js';

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
for (const marker of ['☐', '☑']) {
  const marked = paragraph([
    schema.text(`${marker} `), schema.text('   Review', [schema.marks.bold.create()]),
    schema.node('image'), schema.node('hardBreak'), schema.text('☐ Keep this one')
  ]);
  const promoted = split([marked], 0, 2, 8);
  assert.deepEqual(promoted.titleSelection, { from: 1, to: 4 });
  assert.deepEqual(promoted.title.content[0].content.map(node => node.text ?? node.type), [
    'Review', 'image', 'hardBreak', '☐ Keep this one'
  ]);
  assert.equal(promoted.title.content[0].content[0].marks[0].type, 'bold');
  assert.equal(!!promoted.completed, marker === '☑');
  assert.deepEqual(promoted.originalTitle.content[0], marked.toJSON());
}
assert.deepEqual(split([paragraph([schema.text('☐  ')])], 0, 3).title.content[0].content, []);
assert.equal(split([paragraph([schema.text('Use ☐ in text')])], 0, 0).title.content[0].content[0].text, 'Use ☐ in text');
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

// Outdenting one populated item cannot silently remove blank siblings.
const spacedDoc=schema.node('doc',null,[schema.node('bulletList',null,[
  item([paragraph([])]),item([paragraph([schema.text('Keep before')])]),item([paragraph([])]),
  item([paragraph([schema.text('Promote')])]),item([paragraph([])]),item([paragraph([schema.text('Keep after')])]),item([paragraph([])])
])]);
let promotePos;spacedDoc.descendants((n,pos)=>{if(n.type.name==='paragraph' && n.textContent==='Promote')promotePos=pos+1;});
const spacedSplit=splitAtSelection({state:EditorState.create({schema,doc:spacedDoc,selection:TextSelection.create(spacedDoc,promotePos) }),getJSON:()=>spacedDoc.toJSON()});
assert.deepEqual(spacedSplit.remainingContent.content[0].content,spacedDoc.toJSON().content[0].content.slice(0,3));
assert.deepEqual(spacedSplit.newTaskContent.content[0].content,spacedDoc.toJSON().content[0].content.slice(4));

// Whole-task Tab adds exactly one level, and outer Shift+Tab reverses it.
const empty = schema.node('doc', null, [paragraph([])]).toJSON();
const contents = [empty, schema.node('doc', null, [schema.node('bulletList', null, [item([paragraph([])]),item([paragraph([])])])]).toJSON(), multiline.toJSON(), nestedDoc.toJSON()];
const titles = [empty, schema.node('doc', null, [rich]).toJSON()];
for (const before of contents) for (const children of contents) for (const title of titles) {
  const inputs = JSON.stringify([before,children,title]);
  const demoted = indentTaskDocuments({content:before},{title,content:children});
  const doc = schema.nodeFromJSON(demoted.content);
  doc.check();
  const siblings = doc.child(0);
  let position = 3;
  for (let index=0;index<demoted.listIndex;index++) position += siblings.child(index).nodeSize;
  const state = EditorState.create({schema,doc,selection:TextSelection.create(doc,position)});
  const promoted = splitAtSelection({state,getJSON:()=>doc.toJSON()});
  assert.deepEqual(schema.nodeFromJSON(promoted.remainingContent).toJSON(), before, 'original parent and blanks are preserved');
  assert.deepEqual(schema.nodeFromJSON(promoted.title).toJSON(), title, 'former title retains marks, image and hard break');
  assert.deepEqual(schema.nodeFromJSON(promoted.newTaskContent).toJSON(), children, 'all descendants return to their original levels');
  assert.deepEqual(promoted.titleSelection,{from:1,to:1});
  assert.equal(JSON.stringify([before,children,title]),inputs, 'roundtrip does not mutate snapshots');
}
