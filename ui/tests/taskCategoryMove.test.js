import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { compileScript, parse } from "@vue/compiler-sfc";
import { createRenderer, getCurrentInstance, h, nextTick, ref } from "vue";
import { saveCoordinator } from "../src/services/saveCoordinator.js";

// Exercise the real TaskItem setup/lifecycle with a DOM-free Vue renderer.
// Editors and the visual template are irrelevant to save ownership during moves.
const componentUrl = new URL("../src/components/TaskItem.vue", import.meta.url);
const { descriptor } = parse(await readFile(componentUrl, "utf8"));
let script = compileScript(descriptor, { id: "category-move" }).content;
script = script.replace(/import (\w+) from "[^"\n]+\.vue";/g, "const $1 = {};");
script = script.replace(/from "([^"]+)"/g, (_, specifier) => {
  const url = specifier.startsWith(".")
    ? new URL(specifier, componentUrl).href
    : import.meta.resolve(specifier);
  return `from ${JSON.stringify(url)}`;
});
const { default: TaskItem } = await import(`data:text/javascript;base64,${Buffer.from(script).toString("base64")}`);
TaskItem.render = () => h("task");

const node = (type) => ({ type, children: [], parent: null });
const renderer = createRenderer({
  createElement: node,
  createText: () => node("text"),
  createComment: () => node("comment"),
  setText() {}, setElementText() {}, patchProp() {},
  parentNode: (child) => child.parent,
  nextSibling: (child) => child.parent?.children[child.parent.children.indexOf(child) + 1] ?? null,
  insert(child, parent, anchor = null) {
    if (child.parent) this.remove(child);
    child.parent = parent;
    const index = anchor ? parent.children.indexOf(anchor) : -1;
    parent.children.splice(index < 0 ? parent.children.length : index, 0, child);
  },
  remove(child) {
    if (child.parent) child.parent.children.splice(child.parent.children.indexOf(child), 1);
    child.parent = null;
  }
});
globalThis.window = { addEventListener() {}, removeEventListener() {} };
globalThis.document = { addEventListener() {}, removeEventListener() {} };
globalThis.ResizeObserver = class { observe() {} disconnect() {} };
const category = ref(1);
const errors = [];
const task = {
  id: "moving-task", page: "main", updatedAt: 1,
  title: { type: "doc", content: [{ type: "paragraph" }] },
  content: { type: "doc", content: [{ type: "bulletList", content: [] }] }
};
const noop = () => {};
const bindings = Object.fromEntries([
  "onComplete", "onDirty", "onCreateBelow", "onTabToPrevious", "onSplitToNewTask",
  "onFocusPrevTaskFromTitle", "onFocusNextTaskFromContent", "onDelete"
].map((name) => [name, noop]));
const app = renderer.createApp({
  render: () => h("dashboard", [0, 1].map((id) => h("category", { key: id },
    category.value === id ? [h(TaskItem, { ...bindings, task, key: task.id, onSave: async () => ({ updatedAt: 2 }) })] : [])))
});
app.config.errorHandler = (error) => errors.push(error.message);
const root = node("root");
try {
  app.mount(root);
  // Earlier destination mounts before later source unmounts; then test the reverse.
  for (const destination of [0, 1, 0]) {
    category.value = destination;
    await nextTick();
    assert.deepEqual(errors, [], "moving a task must not throw during component setup or mounting");
    const columns = root.children[0].children;
    assert.equal(columns[destination].children[0]?.type, "task", "the destination renders its task immediately");
    assert.equal(columns[1 - destination].children.length, 0);
    const states = saveCoordinator.getSummary().tasks;
    assert.equal(states.length, 1);
    assert.equal(states[0].attached, true, "the destination owns the save registration");
  }
} finally {
  app.unmount();
  await nextTick();
  delete globalThis.window;
  delete globalThis.document;
  delete globalThis.ResizeObserver;
}
assert.equal(saveCoordinator.getSummary().tasks.length, 0);
console.log("Task category move lifecycle passed");

// A newly created People row already has its focus prop when it mounts.
// Changing an existing prop is insufficient: both mount-time focus routes matter.
globalThis.window = { addEventListener() {}, removeEventListener() {} };
globalThis.document = { addEventListener() {}, removeEventListener() {} };
globalThis.ResizeObserver = class { observe() {} disconnect() {} };
const focusCalls = [];
const EditorStub = {
  setup(_, { expose }) {
    expose({ focus: (selection) => focusCalls.push(selection || "title"), focusListItem: (index, place) => focusCalls.push([index, place]) });
    return () => h("editor");
  }
};
TaskItem.render = function () {
  const state = getCurrentInstance().setupState;
  return h("task", [
    h(EditorStub, { ref: (value) => { state.titleEditorRef = value; } }),
    h(EditorStub, { ref: (value) => { state.contentEditorRef = value; } })
  ]);
};
for (const focusProps of [{ focusTitleId: task.id }, { focusContentTarget: { taskId: task.id, listIndex: 2, atEnd: true } }, { focusTitleId: { taskId: task.id, selection: { from: 5, to: 5 } } }]) {
  const focusApp = renderer.createApp({ render: () => h(TaskItem, { ...bindings, task, ...focusProps, onSave: async () => ({ updatedAt: 2 }) }) });
  focusApp.config.errorHandler = (error) => errors.push(error.message);
  focusApp.mount(node("root"));
  await nextTick();
  await nextTick();
  focusApp.unmount();
}
assert.deepEqual(errors, []);
assert.deepEqual(focusCalls, ["title", [2, "end"], { from: 5, to: 5 }]);
delete globalThis.window;
delete globalThis.document;
delete globalThis.ResizeObserver;
console.log("New task title and subcontent receive focus on mount");

// Exercise blur dismissal and selection-grip dragging through the real component.
const windowListeners = new Map();
const documentListeners = new Map();
globalThis.window = {
  addEventListener: (name, callback) => windowListeners.set(name, callback),
  removeEventListener: (name) => windowListeners.delete(name)
};
globalThis.document = {
  addEventListener: (name, callback) => documentListeners.set(name, callback),
  removeEventListener: (name) => documentListeners.delete(name)
};
globalThis.ResizeObserver = class { observe() {} disconnect() {} };
let actionState;
const metaElement = { style: {}, inert: false };
TaskItem.render = () => {
  actionState = getCurrentInstance().setupState;
  actionState.taskMeta = metaElement;
  return h('task');
};
const dragged = [];
const actionFocus = ref(null);
const actionApp = renderer.createApp({ render: () => h(TaskItem, {
  ...bindings, task, focusTitleId: actionFocus.value, draggable: true, onSave: async () => ({ updatedAt: 2 }),
  onDragStart: (item) => dragged.push(item.id)
}) });
actionApp.mount(node('root'));
actionState.sendOpen = true;
actionState.eventsOpen = true;
windowListeners.get('blur')();
assert.equal(actionState.actionsDismissed, true);
assert.equal(metaElement.style.display, 'none', 'hide synchronously before native activation hit testing');
assert.equal(metaElement.inert, true);
assert.equal(actionState.sendOpen, false);
assert.equal(actionState.eventsOpen, false);
const returningClick = { type: 'click' };
actionState.positionTaskOverlay();
assert.equal(actionState.actionsDismissed, true, 'restored focus/positioning cannot reopen controls before hit testing');
assert.equal(metaElement.style.display, 'none');
actionState.actionDismissal.interact(returningClick);
assert.equal(actionState.actionsDismissed, false, 'the first completed task click resumes its actions');
assert.equal(actionState.sendOpen, false, 'restoring the bar must not reopen its old menus');
assert.equal(actionState.eventsOpen, false);
const { createTaskActionDismissal } = await import('../src/utils/taskActionDismissal.js');
let otherTaskDismissed = false;
const otherTaskActions = createTaskActionDismissal(value => { otherTaskDismissed = value; });
otherTaskActions.blur();
actionState.actionDismissal.interact(returningClick);
assert.equal(otherTaskDismissed, true, 'clicking one task must not restore another task bar');
windowListeners.get('blur')();
actionState.actionDismissal.interact({ type: 'keydown', key: 'a' });
assert.equal(actionState.actionsDismissed, false, 'deliberate keyboard editing resumes actions');
let prevented = false;
actionState.handleDragStart({ target: { closest: (selector) => selector.includes('.task-select-handle') }, preventDefault() { prevented = true; } });
assert.deepEqual(dragged, [task.id], 'the selection grip starts normal task reordering');
assert.equal(prevented, false);
actionState.handleDragStart({ target: { closest: (selector) => selector.includes('.ProseMirror') }, preventDefault() { prevented = true; } });
assert.equal(prevented, true, 'editor text cannot start task reordering');
assert.equal(dragged.length, 1);
assert.equal(actionState.dragging,true);
actionState.handleDragEnd();
assert.equal(actionState.dragging,false);
// A Delete/merge focus request must resume the destination's dismissed controls.
windowListeners.get('blur')();
assert.equal(actionState.actionsDismissed,true);
actionFocus.value={taskId:task.id,selection:{from:1,to:1}};
await nextTick(); await nextTick(); await nextTick();
assert.equal(actionState.actionsDismissed,false);
assert.equal(actionState.sendOpen,false);
assert.equal(actionState.categoryOpen,false);

// Moving a row without resizing it still repositions the fixed bar on update.
globalThis.innerWidth=1000; globalThis.innerHeight=800;
let rowTop=200;
metaElement.getBoundingClientRect=()=>({height:18});
actionState.taskRoot={matches:()=>true,closest:()=>null,getBoundingClientRect:()=>({left:10,right:310,top:rowTop,bottom:rowTop+20})};
actionState.positionTaskOverlay();
assert.equal(actionState.overlayStyle.top,'182px');
rowTop=160;
actionFocus.value={taskId:task.id,selection:{from:1,to:1}};
await nextTick(); await nextTick(); await nextTick();
assert.equal(actionState.overlayStyle.top,'142px');
actionState.taskRoot=null;
delete globalThis.innerWidth; delete globalThis.innerHeight;
actionApp.unmount();
assert.equal(windowListeners.has('blur'), false);
assert.equal(documentListeners.has('click'), false);
delete globalThis.window;
delete globalThis.document;
delete globalThis.ResizeObserver;
console.log('Task action dismissal and selection-grip reordering passed');

// Keyboard intent must survive both the debounce window and an in-flight create.
const { useTaskEditing } = await import('../src/composables/useTaskEditing.js');
const titleDoc = { type: 'doc', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'Original' }] }] };
const itemDoc = (text) => ({ type: 'listItem', content: [{ type: 'paragraph', content: [{ type: 'text', text }] }] });
for (const inFlight of [false, true]) {
  let handlers, finishCreate, timer;
  const moves = [];
  const content = ref({ type: 'doc', content: [{ type: 'bulletList', content: [itemDoc('First'), itemDoc('Second')] }] });
  const harness = renderer.createApp({ setup() {
    handlers = useTaskEditing({
      props: { task: { id: 'original' }, onCreateBelow: () => new Promise(resolve => { finishCreate = resolve; }), onTabToPrevious: async task => { moves.push(task.id); return true; } },
      titleRef: ref(titleDoc), contentRef: content,
      titleEditorRef: ref(null), contentEditorRef: ref({ focusListItem() {} }), hasSubcontent: ref(true),
      revealContent() {}, saveNow: async () => true
    });
    return () => h('test');
  } });
  harness.mount(node('root'));
  const realTimer = globalThis.setTimeout;
  const realClear = globalThis.clearTimeout;
  globalThis.setTimeout = callback => { timer = callback; return 1; };
  globalThis.clearTimeout = () => { timer = null; };
  try {
    const paragraph = { type: { name: 'paragraph' } };
    const editor = { state: { doc: { textContent: 'Original' }, selection: { empty: true, $from: { parent: paragraph, pos: 9, end: () => 9 }, $to: { parent: paragraph } } } };
    handlers.handleTitleKeydown({ key: 'Enter', preventDefault() {} }, editor);
    if (inFlight) timer();
    handlers.handleTitleKeydown({ key: 'Tab', preventDefault() {} }, editor);
    if (inFlight) { finishCreate('new-task'); await Promise.resolve(); await Promise.resolve(); assert.deepEqual(moves, ['new-task']); }
    else { await nextTick(); assert.deepEqual(moves, []); assert.equal(content.value.content[0].content.length, 3); assert.match(JSON.stringify(content.value), /First/); assert.match(JSON.stringify(content.value), /Second/); }
  } finally {
    harness.unmount(); globalThis.setTimeout = realTimer; globalThis.clearTimeout = realClear;
  }
}
console.log('Fast Enter/Tab preserves the source and targets the intended new line');

for (const key of ['Delete', 'Backspace']) {
  let handlers, deleted = 0, merged = 0, deleteDirection;
  const empty = {type:'doc',content:[{type:'paragraph'}]};
  const content = ref(empty);
  const harness = renderer.createApp({ setup() {
    handlers = useTaskEditing({props:{task:{id:'empty'},onDelete:(_task,options)=>{deleted++;deleteDirection=options.direction;},onMergeToPrevious:()=>merged++},titleRef:ref(empty),contentRef:content,titleEditorRef:ref(null),contentEditorRef:ref(null),hasSubcontent:ref(false),revealContent(){},saveNow:async()=>true});
    return ()=>h('test');
  }});
  harness.mount(node('root'));
  const editor = {state:{doc:{textContent:''},selection:{empty:true,$from:{pos:1,start:()=>1,parentOffset:0,depth:1},$to:{pos:1}}}};
  assert.equal(handlers.handleTitleKeydown({key,preventDefault(){}},editor),true);
  assert.equal(deleted,1,`${key} removes an entirely empty task`);
  assert.equal(deleteDirection,key === "Delete" ? "forward" : "backward");
  content.value=titleDoc;
  handlers.handleTitleKeydown({key,preventDefault(){}},editor);
  assert.equal(deleted,1,`${key} preserves a task with subcontent`);
  if(key==='Delete') assert.equal(merged,0,'Delete does not merge nonempty tasks');
  harness.unmount();
}

// Structural edits use the editor's acknowledged revision, not stale rendered props.
let mergeHandlers, mergedSnapshot;
const revisionHarness = renderer.createApp({setup(){
  mergeHandlers=useTaskEditing({props:{task:{id:'revision',updatedAt:1},onMergeToPrevious:task=>{mergedSnapshot=task;}},getTaskSnapshot:()=>({id:'revision',updatedAt:2,title:titleDoc}),titleRef:ref(titleDoc),contentRef:ref({type:'doc',content:[{type:'paragraph'}]}),titleEditorRef:ref(null),contentEditorRef:ref(null),hasSubcontent:ref(false),saveNow:async()=>true});
  return ()=>h('test');
}});
revisionHarness.mount(node('root'));
mergeHandlers.handleTitleKeydown({key:'Backspace',preventDefault(){}},{state:{doc:{textContent:'Original'},selection:{empty:true,$from:{pos:1,start:()=>1,parentOffset:0,parent:{content:{size:8}},depth:1,index:()=>0,node:()=>({childCount:1})}}}});
await Promise.resolve();
assert.equal(mergedSnapshot.updatedAt,2);
revisionHarness.unmount();

// Real ProseMirror selections exercise Delete boundary routing and local joins.
const { Schema } = await import('prosemirror-model');
const { EditorState, TextSelection } = await import('prosemirror-state');
const boundarySchema=new Schema({nodes:{doc:{content:'block+'},paragraph:{group:'block',content:'inline*'},bulletList:{group:'block',content:'listItem+'},listItem:{content:'paragraph block*'},text:{group:'inline'}}});
const boundaryTitle={type:'doc',content:[{type:'paragraph',content:[{type:'text',text:'Title'}]}]};
const boundaryContent={type:'doc',content:[{type:'bulletList',content:[itemDoc('First'),itemDoc('Second')]}]};
for (const mode of ['title-content','title-next','content-next','content-middle','selection','read-only']) {
  let handlers, nextCalls=0, titleCaret;
  const title=ref(structuredClone(boundaryTitle));
  const content=ref(mode==='title-next'?{type:'doc',content:[{type:'paragraph'}]}:structuredClone(boundaryContent));
  const harness=renderer.createApp({setup(){
    handlers=useTaskEditing({props:{task:{id:'boundary'},readOnly:mode==='read-only',onMergeWithNext:async()=>{nextCalls++;}},titleRef:title,contentRef:content,titleEditorRef:ref({focus:selection=>{titleCaret=selection;}}),contentEditorRef:ref(null),hasSubcontent:ref(true),saveNow:async()=>true,onContentChanged(){}});
    return ()=>h('test');
  }});harness.mount(node('root'));
  const inContent=mode.startsWith('content');
  const doc=boundarySchema.nodeFromJSON(inContent?boundaryContent:boundaryTitle);
  let end=0;doc.descendants((node,pos)=>{if(node.type.name==='paragraph') end=pos+1+node.content.size;});
  const position=mode==='content-middle'?8:end;
  const state=EditorState.create({schema:boundarySchema,doc,selection:TextSelection.create(doc,mode==='selection'?1:position,position)});
  const editor={state};
  const handled=(inContent?handlers.handleContentKeydown:handlers.handleTitleKeydown)({key:'Delete',preventDefault(){}},editor);
  await nextTick(); await nextTick(); await nextTick();
  if(mode==='title-content') {
    assert.equal(boundarySchema.nodeFromJSON(title.value).textContent,'TitleFirst');
    assert.equal(boundarySchema.nodeFromJSON(content.value).textContent,'Second');
    assert.deepEqual(titleCaret,{from:6,to:6});
    assert.equal(nextCalls,0);
  } else if(mode==='title-next'||mode==='content-next') assert.equal(nextCalls,1);
  else { assert.equal(handled,false); assert.equal(nextCalls,0); }
  harness.unmount();
}

// Real selections: horizontal arrows cross only exact document boundaries.
for (const area of ['title','content']) {
  for (const direction of [-1,1]) {
    for (const mode of ['edge','middle','selection','shift','ctrl','readOnly']) {
      const calls=[];
      let handlers;
      const harness=renderer.createApp({setup(){
        handlers=useTaskEditing({props:{task:{id:'arrow'},readOnly:mode==='readOnly',onNavigateHorizontal:(task,delta)=>calls.push(['task',task.id,delta])},
          titleRef:ref(boundaryTitle),contentRef:ref(boundaryContent),hasSubcontent:ref(true),saveNow:async()=>true,
          titleEditorRef:ref({focus:selection=>calls.push(['title',selection])}),contentEditorRef:ref({focusListItem:(...args)=>calls.push(['content',...args])})});
        return ()=>h('test');
      }}); harness.mount(node('root'));
      const doc=boundarySchema.nodeFromJSON(area==='title'?boundaryTitle:boundaryContent);
      let start, end; doc.descendants((node,pos)=>{if(node.type.name==='paragraph') { start ??= pos+1; end=pos+1+node.content.size; }});
      const edge=direction<0?start:end;
      const pos=mode==='middle'?start+1:edge;
      const editor={state:EditorState.create({schema:boundarySchema,doc,selection:TextSelection.create(doc,mode==='selection'?start:pos,mode==='selection'?end:pos)})};
      const handled=(area==='title'?handlers.handleTitleKeydown:handlers.handleContentKeydown)({key:direction<0?'ArrowLeft':'ArrowRight',shiftKey:mode==='shift',ctrlKey:mode==='ctrl',preventDefault(){}},editor);
      assert.equal(handled,mode==='edge',`${area} ${direction} ${mode}`);
      if(mode==='edge') {
        if(area==='title' && direction>0) assert.deepEqual(calls,[['content',0,'start']]);
        else if(area==='content' && direction<0) assert.deepEqual(calls,[['title',{from:6,to:6}]]);
        else assert.deepEqual(calls,[['task','arrow',direction]]);
      } else assert.deepEqual(calls,[]);
      harness.unmount();
    }
  }
}
console.log('Horizontal navigation preserves selections and modified arrows, and crosses exact boundaries only');
