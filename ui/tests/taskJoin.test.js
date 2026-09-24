import assert from 'node:assert/strict';
import { Schema } from 'prosemirror-model';
import { EditorState, TextSelection } from 'prosemirror-state';
import { joinTaskDocuments, joinFirstContentLine, isAtDocumentEdge, lastParagraph } from '../src/utils/taskJoin.js';
const schema=new Schema({nodes:{doc:{content:'block+'},paragraph:{group:'block',content:'inline*'},bulletList:{group:'block',content:'listItem+'},listItem:{content:'paragraph block*'},text:{group:'inline'},hardBreak:{group:'inline',inline:true},image:{group:'inline',inline:true,atom:true}},marks:{bold:{},link:{attrs:{href:{}}}}});
const p=text=>({type:'paragraph',content:text ? [{type:'text',text}] : []});
const doc=text=>({type:'doc',content:[p(text)]});
const item=(text,children=[])=>({type:'listItem',content:[p(text),...children]});
const list=items=>({type:'bulletList',content:items});
const content=items=>({type:'doc',content:[list(items)]});
const upper={title:doc('Upper'),content:doc('')};
const lower={title:doc('Lower'),content:content([item('Keep lower child')])};
let joined=joinTaskDocuments(upper,lower);
assert.equal(schema.nodeFromJSON(joined.title).textContent,'UpperLower');
assert.equal(joined.area,'title'); assert.equal(joined.selection.from,6);
assert.deepEqual(joined.content,lower.content);
upper.content=content([item('Parent',[list([item('Nested 😀')])])]);
const original=structuredClone(upper);
joined=joinTaskDocuments(upper,lower);
assert.deepEqual(upper,original,'pure join does not mutate the source/undo snapshot');
assert.deepEqual(joined.title,upper.title);
const mergedDoc=schema.nodeFromJSON(joined.content);
mergedDoc.check();
const caret=mergedDoc.resolve(joined.selection.from);
assert.equal(caret.parent.textContent,'Nested 😀Lower');
assert.equal(caret.parentOffset,'Nested 😀'.length);
assert.equal(joined.content.content[0].content[1].content[0].content[0].text,'Keep lower child');
assert.equal(joined.area,'content');

const rich={type:'text',text:'Linked',marks:[{type:'link',attrs:{href:'https://example.com'}}]};
const multi=content([{type:'listItem',content:[{type:'paragraph',content:[rich,{type:'hardBreak'},{type:'image'}, {type:'text',text:'Remaining'}]},p('Second paragraph'),list([item('Nested')])]},item('Next row')]);
let first=joinFirstContentLine(doc('Title'),multi);
assert.equal(schema.nodeFromJSON(first.title).textContent,'TitleLinked');
assert.deepEqual(first.title.content[0].content[1],rich);
assert.equal(first.content.content[0].content[0].content[0].content[0].type,'image');
schema.nodeFromJSON(first.content).check();
assert.deepEqual(first.selection,{from:6,to:6});
first=joinFirstContentLine(doc('Title'),content([item('First',[list([item('Child')])]),item('Last')]));
assert.equal(schema.nodeFromJSON(first.content).textContent,'ChildLast');
schema.nodeFromJSON(first.content).check();
assert.equal(joinFirstContentLine(doc('Title'),doc('')),null);

const nested=schema.nodeFromJSON(upper.content);
const end=lastParagraph(upper.content).end;
const editor=position=>({state:EditorState.create({schema,doc:nested,selection:TextSelection.create(nested,position)})});
assert.equal(isAtDocumentEdge(editor(end),true),true);
assert.equal(isAtDocumentEdge(editor(end-1),true),false);
assert.equal(isAtDocumentEdge(editor(3+'Parent'.length),true),false,'end of parent is not end of nested content');
const selected={state:EditorState.create({schema,doc:nested,selection:TextSelection.create(nested,end-1,end)})};
assert.equal(isAtDocumentEdge(selected,true),false,'selection deletion stays native');
console.log('Boundary joins preserve nested content, rich marks, hard breaks and exact caret positions');

const { listItemFocusPosition } = await import('../src/utils/taskNavigation.js');
const focusSchema=new Schema({nodes:{doc:{content:'block+'},paragraph:{group:'block',content:'inline*'},bulletList:{group:'block',content:'listItem+'},listItem:{content:'paragraph block*'},text:{group:'inline'}}});
const focusDoc=focusSchema.nodeFromJSON({type:'doc',content:[{type:'bulletList',content:[{type:'listItem',content:[{type:'paragraph',content:[{type:'text',text:'First'}]},{type:'bulletList',content:[{type:'listItem',content:[{type:'paragraph',content:[{type:'text',text:'Nested'}]}]}]}]},{type:'listItem',content:[{type:'paragraph'}]}]}]});
for(const index of [0,1]) {
  for(const place of ['start','end']) {
    const pos=listItemFocusPosition(focusDoc,index,place);
    const resolved=focusDoc.resolve(pos);
    assert.equal(resolved.parent.type.name,'paragraph');
    assert.equal(resolved.parent.textContent,index===0?'First':'');
    assert.equal(resolved.parentOffset,place==='start'?0:resolved.parent.content.size);
  }
}
assert.equal(listItemFocusPosition(focusDoc,99),null);
console.log('List focus targets the first paragraph exactly, including empty and nested items');

const { verticalCaretIntent, verticalFocusPosition } = await import('../src/utils/taskNavigation.js');
for (const [offset,edge] of [[0,'start'],[2,'middle'],[5,'end']]) {
  const intent=verticalCaretIntent({state:{selection:{$from:{parentOffset:offset,parent:{content:{size:5}},pos:3+offset}}},view:{coordsAtPos:()=>({left:125})}});
  assert.equal(intent.edge,edge);
  if(edge==='middle') assert.equal(intent.x,125);
}
assert.equal(verticalCaretIntent({state:{selection:{$from:{parentOffset:0,parent:{content:{size:0}}}}}}).edge,'start');
const verticalDoc=focusSchema.nodeFromJSON({type:'doc',content:[{type:'paragraph',content:[{type:'text',text:'First line'}]},{type:'paragraph',content:[{type:'text',text:'Last line'}]}]});
const verticalEditor={state:{doc:verticalDoc},view:{coordsAtPos:pos=>({left:100,top:pos<12?10:30,bottom:pos<12?22:42}),posAtCoords:point=>({pos:point.top<30?5:16})}};
assert.equal(verticalFocusPosition(verticalEditor,{direction:1,edge:'start'}),1);
assert.equal(verticalFocusPosition(verticalEditor,{direction:1,edge:'end'}),11);
assert.equal(verticalFocusPosition(verticalEditor,{direction:-1,edge:'start'}),13);
assert.equal(verticalFocusPosition(verticalEditor,{direction:-1,edge:'end'}),22);
assert.equal(verticalFocusPosition(verticalEditor,{direction:1,edge:'middle',x:125}),5);
assert.equal(verticalFocusPosition(verticalEditor,{direction:-1,edge:'middle',x:125}),16);
verticalEditor.view.posAtCoords=()=>({pos:999});
assert.equal(verticalFocusPosition(verticalEditor,{direction:1,edge:'middle',x:900}),11,'short destination clamps to its end');
console.log('Cross-task vertical focus preserves start/end and the nearest visual horizontal position');
