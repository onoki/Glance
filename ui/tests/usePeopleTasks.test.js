import assert from "node:assert/strict";
import { ref } from "vue";
import { usePeopleTasks } from "../src/composables/usePeopleTasks.js";

const emptyDoc = () => ({ type: "doc", content: [{ type: "paragraph" }] });
const textDoc = (text) => ({
  type: "doc",
  content: [{ type: "paragraph", content: [{ type: "text", text }] }]
});

const store = [];
const deletedStore = new Map();
let nextId = 1;
let clock = 1000;
const clone = (value) => JSON.parse(JSON.stringify(value));
const api = {
  fetchPersonTasks: async (personId) => ({
    tasks: store.filter((task) => task.ownerPersonId === personId).map(clone)
  }),
  createTask: async (payload) => {
    const id = `task-${nextId++}`;
    const updatedAt = ++clock;
    store.push({ ...clone(payload), id, updatedAt, completedAt: null });
    return { taskId: id, updatedAt };
  },
  updateTask: async (id, payload) => {
    const task = store.find((item) => item.id === id);
    Object.assign(task, clone(payload), { updatedAt: ++clock });
    return { updatedAt: task.updatedAt };
  },
  deleteTask: async (id) => {
    const index = store.findIndex((task) => task.id === id);
    if (index >= 0) {
      deletedStore.set(id, clone(store[index]));
      store.splice(index, 1);
    }
    return { ok: true };
  },
  restoreTask: async (id) => {
    const task = deletedStore.get(id);
    if (!task) throw new Error("not deleted");
    const updatedAt = ++clock;
    store.push({ ...clone(task), updatedAt });
    deletedStore.delete(id);
    return { updatedAt };
  },
  completeTask: async (id, { completed }) => {
    const task = store.find((item) => item.id === id);
    task.completedAt = completed ? ++clock : null;
    return { completedAt: task.completedAt };
  }
};

const personId = ref("person-1");
let historyChanges = 0;
const peopleTasks = usePeopleTasks({
  selectedPersonId: personId,
  onHistoryChange: () => { historyChanges += 1; },
  api
});

await peopleTasks.loadTasks();
assert.equal(peopleTasks.tasks.value.length, 1, "an empty writable row is created for an empty person list");
assert.equal(peopleTasks.focusTaskId.value, peopleTasks.tasks.value[0].id);

const first = peopleTasks.tasks.value[0];
await peopleTasks.saveTask({
  id: first.id,
  title: textDoc("Question"),
  content: emptyDoc(),
  baseUpdatedAt: first.updatedAt
});
await peopleTasks.toggleComplete(first);
assert.equal(peopleTasks.tasks.value.filter((task) => !!task.completedAt).length, 1, "completed-today item stays visible");
assert.equal(peopleTasks.tasks.value.filter((task) => !task.completedAt).length, 1, "a fresh empty row is available after completion");

const completed = peopleTasks.tasks.value.find((task) => !!task.completedAt);
await peopleTasks.toggleComplete(completed);
assert.equal(peopleTasks.tasks.value.find((task) => task.id === completed.id).completedAt, null, "completion can be undone in place");
assert.equal(historyChanges, 2);

const second = peopleTasks.tasks.value.find((task) => task.id !== completed.id);
peopleTasks.startDrag(completed, null);
peopleTasks.setDragOver(second.id, "after");
await peopleTasks.dropOnTask(second, "people", null);
assert.ok(
  peopleTasks.tasks.value.find((task) => task.id === completed.id).position > second.position,
  "dragging after another note persists a later position"
);

const dirtyBlank = peopleTasks.tasks.value.find((task) => task.id === second.id);
peopleTasks.handleDirtyChange(dirtyBlank.id, true, {
  ...dirtyBlank,
  title: textDoc("Typed before checking")
});
await peopleTasks.toggleComplete(dirtyBlank);
const completedDirtyTask = store.find((task) => task.id === dirtyBlank.id);
assert.equal(completedDirtyTask.title.content[0].content[0].text, "Typed before checking");
assert.ok(completedDirtyTask.completedAt, "a newly typed row is saved and completed instead of deleted as empty");

const previous = peopleTasks.tasks.value.find((task) => task.id === completed.id);
const createdId = await peopleTasks.createTaskBelow(previous);
const current = peopleTasks.tasks.value.find((task) => task.id === createdId);
peopleTasks.handleDirtyChange(current.id, true, {
  ...current,
  title: textDoc("Typed immediately before Tab")
});
await peopleTasks.moveTaskToPrevious(current);
const mergedPrevious = store.find((task) => task.id === previous.id);
assert.match(
  JSON.stringify(mergedPrevious.content),
  /Typed immediately before Tab/,
  "Tab merge uses the current dirty editor snapshot"
);
assert.equal(store.some((task) => task.id === current.id), false);

const undoTargetId = await peopleTasks.createTaskBelow(previous);
const undoTarget = peopleTasks.tasks.value.find((task) => task.id === undoTargetId);
await peopleTasks.saveTask({
  id: undoTarget.id,
  title: textDoc("Restore the same row"),
  content: emptyDoc(),
  baseUpdatedAt: undoTarget.updatedAt
});
await peopleTasks.removeTask(undoTarget);
assert.equal(store.some((task) => task.id === undoTargetId), false, "delete hides the task");
await peopleTasks.undo();
assert.equal(store.some((task) => task.id === undoTargetId), true, "undo restores the same task id");
await peopleTasks.redo();
assert.equal(store.some((task) => task.id === undoTargetId), false, "redo deletes the restored task again");
