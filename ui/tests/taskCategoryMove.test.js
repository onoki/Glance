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
