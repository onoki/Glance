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
const actionApp = renderer.createApp({ render: () => h(TaskItem, {
  ...bindings, task, draggable: true, onSave: async () => ({ updatedAt: 2 }),
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
