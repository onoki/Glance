import assert from "node:assert/strict";
import { Schema } from "prosemirror-model";
import { EditorState, TextSelection, AllSelection } from "prosemirror-state";
import { history, undo, redo } from "prosemirror-history";
import { handleEmptyListItemBackspace, appendListItem } from "../src/utils/editorListUtils.js";
const schema = new Schema({nodes:{
  doc:{content:'block+'},paragraph:{group:'block',content:'inline*'},
  bulletList:{group:'block',content:'listItem+'},listItem:{content:'paragraph block*'},
  taskList:{group:'block',content:'taskItem+'},taskItem:{content:'paragraph block*'},
  text:{group:'inline'},image:{inline:true,group:'inline',atom:true},hardBreak:{inline:true,group:'inline'}
},marks:{bold:{},link:{attrs:{href:{}}}}});
const p = text => schema.node('paragraph',null,text ? schema.text(text,[schema.marks.bold.create()]) : []);
function editorFor(doc, pos, to=pos) {
  const editor={state:EditorState.create({schema,doc,selection:TextSelection.create(doc,pos,to),plugins:[history()]})};
  editor.view={dispatch:tr=>{editor.state=editor.state.apply(tr);editor.state.doc.check();}};
  editor.commands={focus(){}};
  return editor;
}
let cases=0;
for(const kind of ['bulletList','taskList']) for(const depth of [1,2,3,4,5]) {
  for(const slot of ['only','first','middle','last']) for(const extraBlock of [false,true]) {
    const itemType=kind==='bulletList'?'listItem':'taskItem';
    const item=children=>schema.node(itemType,null,children);
    let items=[item([p('')])];
    if(['middle','last'].includes(slot)) items.unshift(item([p('Before KEEP')]));
    if(['middle','first'].includes(slot)) items.push(item([p('After KEEP')]));
    let list=schema.node(kind,null,items);
    for(let level=1;level<depth;level++) list=schema.node(kind,null,[item([p(`Parent ${level} KEEP`),list]),item([p(`Sibling ${level} KEEP`)])]);
    const doc=schema.node('doc',null,extraBlock?[p('Outside KEEP'),list,p('Tail KEEP')]:[list]);
    let pos;doc.descendants((n,at)=>{if(n.type.name==='paragraph' && !n.content.size) pos=at+1;});
    let precedingEnd = null;
    doc.descendants((n, at) => {
      if (n.isTextblock && at + 1 < pos) precedingEnd = at + 1 + n.content.size;
    });
    const editor=editorFor(doc,pos);
    assert.equal(handleEmptyListItemBackspace(editor),true);
    assert.equal(editor.state.doc.textContent,doc.textContent,`${kind}/${depth}/${slot}: unrelated text must survive`);
    assert.ok(editor.state.selection.$from.parent.isTextblock,'caret must resolve inside text');
    if (precedingEnd !== null) {
      assert.equal(editor.state.selection.from, precedingEnd, `${kind}/${depth}/${slot}: Backspace returns to preceding line end`);
    } else {
      assert.equal(editor.state.selection.$from.parentOffset, 0, 'without a preceding line, use the first remaining line start');
    }
    const after=editor.state.doc.toJSON();
    assert.equal(undo(editor.state,editor.view.dispatch),true);
    assert.deepEqual(editor.state.doc.toJSON(),doc.toJSON(),'Undo restores complete hierarchy and marks');
    assert.equal(redo(editor.state,editor.view.dispatch),true);
    assert.deepEqual(editor.state.doc.toJSON(),after);
    // A ranged selection is native deletion, never custom empty-item handling.
    const selected=editorFor(doc,pos);
    selected.view.dispatch(selected.state.tr.setSelection(new AllSelection(doc)));
    assert.equal(handleEmptyListItemBackspace(selected),false);
    assert.deepEqual(selected.state.doc.toJSON(),doc.toJSON());
    cases++;
  }
}
// A cleared parent line with populated descendants is not an empty item.
const parentDoc=schema.node('doc',null,[schema.node('bulletList',null,[schema.node('listItem',null,[p(''),schema.node('bulletList',null,[schema.node('listItem',null,[p('Descendant KEEP')])])])])]);
assert.equal(handleEmptyListItemBackspace(editorFor(parentDoc,3)),false);
const imageDoc=schema.node('doc',null,[schema.node('bulletList',null,[schema.node('listItem',null,[schema.node('paragraph',null,[schema.node('image')])])])]);
assert.equal(handleEmptyListItemBackspace(editorFor(imageDoc,3)),false,'image-only bullets are not empty');
// Noncanonical but schema-valid imported/pasted paragraphs must not be overwritten by Enter.
const mixed=schema.node('doc',null,[p('Imported KEEP'),p('More KEEP')]);
const editor=editorFor(mixed,1);
appendListItem(editor);
assert.equal(editor.state.doc.textContent,mixed.textContent,'appending a list must retain non-list blocks');
console.log(`Nested-list safety: ${cases} depth/sibling/list-type cases, selections, images, mixed blocks, Undo/Redo`);
