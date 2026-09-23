import assert from "node:assert/strict";
import { createTagColorSaver } from "../src/utils/tagColor.js";

// The native picker streams input events; the final color must win on disk.
const colorWrites = [];
let finishFirstColor;
const saveColor = createTagColorSaver(async (id, color) => {
  colorWrites.push([id, color]);
  if (colorWrites.length === 1) await new Promise(resolve => { finishFirstColor = resolve; });
});
const firstColor = saveColor('team', '#112233');
const middleColor = saveColor('team', '#445566');
const lastColor = saveColor('team', '#778899');
await saveColor('other', '#abcdef');
finishFirstColor();
assert.deepEqual(await Promise.all([firstColor, middleColor, lastColor]), ['#778899', '#778899', '#778899']);
assert.deepEqual(colorWrites, [['team', '#112233'], ['other', '#abcdef'], ['team', '#778899']]);
let failColor = true;
const retryColor = createTagColorSaver(async () => { if (failColor) throw new Error('offline'); });
await assert.rejects(retryColor('team', '#112233'), /offline/);
failColor = false;
assert.equal(await retryColor('team', '#445566'), '#445566', 'failed saves must allow a later retry');
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
await peopleTasks.undo();
assert.match(JSON.stringify(store.find(task => task.id === current.id).title), /Typed immediately before Tab/, "undoing a merge restores its source text");
assert.doesNotMatch(JSON.stringify(store.find(task => task.id === previous.id).content), /Typed immediately before Tab/);
await peopleTasks.redo();
assert.equal(store.some(task => task.id === current.id), false);
assert.match(JSON.stringify(store.find(task => task.id === previous.id).content), /Typed immediately before Tab/);

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

const editTargetId = await peopleTasks.createTaskBelow();
const editTarget = peopleTasks.tasks.value.find(task => task.id === editTargetId);
await peopleTasks.saveTask({ id: editTargetId, title: textDoc("Before"), content: emptyDoc(), baseUpdatedAt: editTarget.updatedAt });
peopleTasks.handleDirtyChange(editTargetId, true, { ...editTarget, title: textDoc("After") });
await peopleTasks.loadTasks(); // Polling must not lose the persisted baseline for undo.
await peopleTasks.saveTask({ id: editTargetId, title: textDoc("After"), content: emptyDoc(), baseUpdatedAt: editTarget.updatedAt });
await peopleTasks.undo();
assert.match(JSON.stringify(store.find(task => task.id === editTargetId).title), /Before/);
await peopleTasks.redo();
assert.match(JSON.stringify(store.find(task => task.id === editTargetId).title), /After/);
store.find(task => task.id === editTargetId).title = textDoc("Changed in another window");
await assert.rejects(() => peopleTasks.undo(), /changed elsewhere/, "undo must not overwrite an external edit");

await peopleTasks.splitSubcontentToNewTask(editTarget, { title: textDoc('Promoted subtask'), content: emptyDoc(), titleSelection: { from: 8, to: 8 } });
assert.deepEqual(peopleTasks.focusTaskId.value.selection, { from: 8, to: 8 });
assert.ok(peopleTasks.tasks.value.some(task => task.id === peopleTasks.focusTaskId.value.taskId));

// Clipboard history belongs to the window, not a mounted People view.
const sharedHistory = { undo: [], redo: [] };
let clipboardUndos = 0;
const firstMount = usePeopleTasks({ selectedPersonId: ref('person-one'), api, history: sharedHistory });
firstMount.recordClipboard({ undo: async () => { clipboardUndos++; }, redo: async () => {} });
const remountedPerson = ref('person-two');
const secondMount = usePeopleTasks({ selectedPersonId: remountedPerson, api, history: sharedHistory });
await secondMount.undo();
assert.equal(clipboardUndos, 1);
assert.equal(remountedPerson.value, 'person-one');

const mergePerson = usePeopleTasks({ selectedPersonId: ref('merge-caret-person'), api });
await mergePerson.loadTasks();
const mergeFirst = mergePerson.tasks.value[0];
await mergePerson.saveTask({id:mergeFirst.id,title:textDoc('First task'),content:emptyDoc(),baseUpdatedAt:mergeFirst.updatedAt});
const mergeSecondId = await mergePerson.createTaskBelow(mergePerson.tasks.value[0]);
const mergeSecond = mergePerson.tasks.value.find(task=>task.id===mergeSecondId);
await mergePerson.saveTask({id:mergeSecond.id,title:textDoc('Second task'),content:emptyDoc(),baseUpdatedAt:mergeSecond.updatedAt});
await mergePerson.mergeTaskToPrevious(mergePerson.tasks.value.find(task=>task.id===mergeSecondId));
assert.deepEqual(mergePerson.focusTaskId.value,{taskId:mergeFirst.id,selection:{from:11,to:11}},'merge caret stays immediately before Second task');
assert.equal(store.find(task=>task.id===mergeFirst.id).title.content[0].content.map(node=>node.text).join(''),'First taskSecond task');

// Delete moves forward to title start; Backspace retains the prior-row route.
const directionPerson=usePeopleTasks({selectedPersonId:ref('direction-person'),api});
await directionPerson.loadTasks();
const firstDirection=directionPerson.tasks.value[0];
const middleDirection=await directionPerson.createTaskBelow(firstDirection);
const finalDirection=await directionPerson.createTaskBelow(directionPerson.tasks.value.find(task=>task.id===middleDirection));
await directionPerson.saveTask({id:finalDirection,title:textDoc('Next title'),content:emptyDoc(),baseUpdatedAt:directionPerson.tasks.value.find(task=>task.id===finalDirection).updatedAt});
await directionPerson.removeTask(directionPerson.tasks.value.find(task=>task.id===middleDirection),{direction:'forward'});
assert.deepEqual(directionPerson.focusTaskId.value,{taskId:finalDirection,selection:{from:1,to:1}});
await directionPerson.removeTask(directionPerson.tasks.value.find(task=>task.id===finalDirection),{direction:'backward'});
assert.equal(directionPerson.focusTaskId.value,firstDirection.id);

// Switching people is atomic and a late response / background poll cannot undo it.
const delayed=new Map();
const selectedSwitch=ref('old');
const switching=usePeopleTasks({selectedPersonId:selectedSwitch,api:{...api,fetchPersonTasks:id=>new Promise(resolve=>delayed.set(id,resolve))}});
switching.tasks.value=[{id:'old-row',ownerPersonId:'old'}];
const firstSwitch=switching.loadTasks('one');
assert.equal(selectedSwitch.value,'old');
assert.equal(switching.tasks.value[0].id,'old-row');
await switching.loadTasks();
assert.equal(delayed.has('old'),false,'poll cannot cancel a user switch');
const secondSwitch=switching.loadTasks('two');
delayed.get('two')({tasks:[{id:'two-row',ownerPersonId:'two',title:textDoc('Two'),content:emptyDoc()}]});
await secondSwitch;
delayed.get('one')({tasks:[{id:'one-row',ownerPersonId:'one',title:textDoc('One'),content:emptyDoc()}]});
await firstSwitch;
assert.equal(selectedSwitch.value,'two');
assert.equal(switching.tasks.value[0].id,'two-row');

// Both directions join at the previous task's final subcontent line, with Undo.
for (const forward of [false,true]) {
  const joinedPerson=usePeopleTasks({selectedPersonId:ref(`join-person-${forward}`),api});
  await joinedPerson.loadTasks();
  const upperId=joinedPerson.tasks.value[0].id;
  const lines={type:'doc',content:[{type:'bulletList',content:[{type:'listItem',content:[{type:'paragraph',content:[{type:'text',text:'Tail'}]}]}]}]};
  await joinedPerson.saveTask({id:upperId,title:textDoc('Upper'),content:lines,baseUpdatedAt:joinedPerson.tasks.value[0].updatedAt});
  const lowerId=await joinedPerson.createTaskBelow(joinedPerson.tasks.value[0]);
  await joinedPerson.saveTask({id:lowerId,title:textDoc('Lower'),content:lines,baseUpdatedAt:joinedPerson.tasks.value.find(task=>task.id===lowerId).updatedAt});
  const source=joinedPerson.tasks.value.find(task=>task.id===(forward?upperId:lowerId));
  await (forward?joinedPerson.mergeTaskWithNext(source):joinedPerson.mergeTaskToPrevious(source));
  const merged=store.find(task=>task.id===upperId);
  assert.equal(merged.title.content[0].content[0].text,'Upper');
  assert.equal(merged.content.content[0].content[0].content[0].content.map(node=>node.text).join(''),'TailLower');
  assert.equal(merged.content.content[0].content.length,2);
  assert.deepEqual(joinedPerson.focusContentTarget.value,{taskId:upperId,selection:{from:7,to:7}});
  assert.equal(joinedPerson.focusTaskId.value,null);
  await joinedPerson.undo();
  assert.ok(store.some(task=>task.id===lowerId));
  assert.deepEqual(store.find(task=>task.id===upperId).content,lines);
  await joinedPerson.redo();
  assert.equal(store.some(task=>task.id===lowerId),false);
}
