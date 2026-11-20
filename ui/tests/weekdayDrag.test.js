import assert from "node:assert/strict";
import { useDashboardDrag } from "../src/composables/useDashboardDrag.js";
import { currentWeekDays, planningWeekDays } from "../src/utils/categoryUtils.js";
import { ref } from "vue";
import { TASK_DRAG_TYPE, weekdayDropTarget } from "../src/utils/taskDrag.js";
import { taskGroupActions, taskGroupChanges } from "../src/utils/taskGroups.js";
const task = { id: "new", page: "dashboard:new" };
const moves = [];
const drag = useDashboardDrag({
  findTaskById: id => id === task.id ? task : null,
  getCategoryTasks: () => [],
  applyTaskMove: (...args) => moves.push(args)
});
const event = { dataTransfer: { getData: () => task.id } };
const sunday = currentWeekDays().at(-1).dateKey;
await drag.dropOnWeekdayFromView("week-test", sunday, event);
assert.equal(moves.length, 1);
assert.equal(moves[0][3], sunday);
await drag.dropOnWeekdayFromView("week-test", "2000-01-01", event);
assert.equal(moves.length, 1, "expired day targets cannot redate tasks");
console.log("Weekday drops accept New tasks and reject expired dates");

// Weekday groups render separately even when their numeric positions interleave.
// Calculate neighbours within the target day so a drop always lands as shown.
const targetDay = currentWeekDays().find(day => !day.isPast).dateKey;
const rows = [
  { id: 'a', page: 'dashboard:main', position: 0, scheduledDate: targetDay },
  { id: 'other-day', page: 'dashboard:main', position: 50, scheduledDate: '2000-01-01' },
  { id: 'b', page: 'dashboard:main', position: 100, scheduledDate: targetDay },
  { id: 'c', page: 'dashboard:main', position: 200, scheduledDate: targetDay }
];
const reorder = useDashboardDrag({ findTaskById: id => rows.find(row => row.id === id), getCategoryTasks: () => rows, applyTaskMove: (...args) => moves.push(args) });
reorder.startDrag(rows[2]);
reorder.setDragOver(rows[2].id, 'after');
assert.equal(reorder.dragOver.value.id, null, 'no insertion marker on the dragged source');
reorder.setDragOver('a', 'after');
await reorder.dropOnTask(rows[0], 'week-test');
assert.equal(moves.at(-1)[2], 100);
assert.equal(moves.at(-1)[3], targetDay);
const grouped = useDashboardDrag({
  findTaskById: id => rows.find(row => row.id === id),
  getCategoryTasks: () => rows,
  applyTaskMoves: (...args) => moves.push(args)
});
grouped.startDrag(rows[3], null, [rows[2], rows[3]]);
grouped.setDragOver('b', 'before');
assert.equal(grouped.dragOver.value.id, null, 'selected rows cannot be their own group target');
grouped.setDragOver('a', 'after');
await grouped.dropOnTask(rows[0], 'week-test');
assert.deepEqual(moves.at(-1), [[rows[2], rows[3]], 'week-test', [1, 2], targetDay]);
reorder.endDrag();
reorder.setDragOver('a', 'before');
assert.equal(reorder.dragOver.value.id, null, 'unrelated text/image drags do not create task markers');

let captures = 0;
let pans = 0;
const pan = useDashboardDrag({ activeTab: ref('Dashboard'), dashboardColumnsRef: ref({scrollLeft:0,setPointerCapture:()=>captures++}) });
pan.handleDashboardPointerDown({button:0,target:{closest:selector=>selector.includes('label')},preventDefault:()=>pans++});
assert.equal(captures,0,'menu labels must never start Dashboard background panning');
assert.equal(pans,0);
pan.handleDashboardPointerDown({button:0,target:{closest:()=>null},clientX:10,pointerId:1,preventDefault:()=>pans++});
assert.equal(captures,1,'background panning remains available');

const nextSunday = planningWeekDays().at(-1).dateKey;
await drag.dropOnWeekdayFromView('week-next', nextSunday, event);
assert.equal(moves.at(-1)[3], nextSunday, 'all seven next-week day targets accept drops');
rows[0].scheduledDate = nextSunday;
rows[2].scheduledDate = nextSunday;
rows[3].scheduledDate = nextSunday;
reorder.startDrag(rows[0]);
await reorder.dropOnWeekdayFromView('week-next', nextSunday, null);
assert.equal(moves.at(-1)[2], 201, 'day background drops use the last task in the target day');

const taskEvent = { dataTransfer: { types: [TASK_DRAG_TYPE] } };
reorder.setWeekdayDragOver(nextSunday, taskEvent);
assert.equal(reorder.dragOverDay.value, nextSunday, 'empty days have their own insertion target');
reorder.clearWeekdayDragOver(nextSunday, { currentTarget: { contains: () => true }, relatedTarget: {} });
assert.equal(reorder.dragOverDay.value, nextSunday, 'crossing header children retains the marker');
reorder.clearWeekdayDragOver(nextSunday, { currentTarget: { contains: () => false, getBoundingClientRect: () => ({left:0,right:100,top:0,bottom:30}) }, clientX:20,clientY:15 });
assert.equal(reorder.dragOverDay.value, nextSunday, 'missing relatedTarget inside the same header must not flicker');
reorder.clearWeekdayDragOver(nextSunday, { currentTarget: { contains: () => false } });
assert.equal(reorder.dragOverDay.value, null);
reorder.setWeekdayDragOver('2000-01-01', taskEvent);
assert.equal(reorder.dragOverDay.value, null, 'past days cannot show a drop marker');
reorder.setWeekdayDragOver(nextSunday, { dataTransfer: { types: ['text/plain'] } });
assert.equal(reorder.dragOverDay.value, null, 'text/image drags cannot create weekday markers');
reorder.setWeekdayDragOver(nextSunday, taskEvent);
reorder.setDragOver('b', 'before');
assert.equal(reorder.dragOverDay.value, null, 'a task marker replaces the day marker');
reorder.setWeekdayDragOver(nextSunday, taskEvent);
assert.equal(reorder.dragOver.value.id, null, 'a day marker replaces the task marker');
await reorder.dropOnWeekdayFromView('week-next', nextSunday);
assert.equal(reorder.dragOverDay.value, null, 'dropping clears the day marker');
reorder.setWeekdayDragOver(nextSunday, taskEvent);
reorder.endDrag();
assert.equal(reorder.dragOverDay.value, null, 'cancel/end clears the day marker');
reorder.setWeekdayDragOver(nextSunday, taskEvent);
assert.equal(reorder.dragOverDay.value, null, 'no marker without an active task drag');

const dayBounds=[{dateKey:'Saturday',top:100},{dateKey:'Sunday',top:140}];
for (const y of [100,115,128,130,139.99]) assert.equal(weekdayDropTarget(dayBounds,y).dateKey,'Saturday');
for (const y of [140,141,180]) assert.equal(weekdayDropTarget(dayBounds,y).dateKey,'Sunday');
assert.equal(weekdayDropTarget(dayBounds,99).dateKey,'Saturday','space above the first heading belongs to the first day');
assert.equal(weekdayDropTarget([],140),null);
assert.equal(weekdayDropTarget(dayBounds,NaN),null);

const groupRows = [
  { id: 'first', groupId: 'group', position: 10 },
  { id: 'middle', groupId: 'group', position: 20 },
  { id: 'last', groupId: 'group', position: 30 },
  { id: 'outside', position: 40 }
];
const bracketDrag = useDashboardDrag({
  findTaskById: id => groupRows.find(row => row.id === id),
  getCategoryTasks: () => groupRows,
  taskGroupRows: () => groupRows,
  applyTaskMove: (...args) => moves.push(args),
  applyTaskMoves: (...args) => moves.push(args)
});
bracketDrag.startDrag(groupRows[3]);
bracketDrag.setDragOver('middle', 'before');
assert.equal(bracketDrag.dragOver.value.id, 'first', 'the outside drop marker snaps to the edge');
await bracketDrag.dropOnTask(groupRows[1], 'uncategorized');
assert.equal(moves.at(-1)[2], 9);
bracketDrag.startDrag(groupRows[0]);
bracketDrag.setDragOver('outside', 'after');
assert.equal(bracketDrag.dragOver.value.id, null, 'a partial group cannot show a marker outside its bracket');
await assert.rejects(bracketDrag.dropOnTask(groupRows[3], 'uncategorized'), /Detach the task first/);
bracketDrag.setDragOver('middle', 'after');
await bracketDrag.dropOnTask(groupRows[1], 'uncategorized');
assert.equal(moves.at(-1)[2], 25, 'a member can still move within its group');
bracketDrag.startDrag(groupRows[0], null, groupRows.slice(0, 3));
bracketDrag.setDragOver('outside', 'after');
await bracketDrag.dropOnTask(groupRows[3], 'uncategorized');
assert.deepEqual(moves.at(-1)[2], [41, 42, 43]);

const tied = [
  { id: 'additional', position: 10 },
  { id: 'between', position: 20 },
  { id: 'group-start', position: 20, groupId: 'original' },
  { id: 'group-end', position: 30, groupId: 'original' },
  { id: 'separate-start', position: 40, groupId: 'separate' },
  { id: 'separate-end', position: 50, groupId: 'separate' }
];
const selection = new Set(['additional', 'group-start', 'group-end']);
const changes = taskGroupChanges(tied, tied[0], selection, 'extend');
assert.deepEqual(changes.map(change => change.task.id), [
  'between', 'additional', 'group-start', 'group-end', 'separate-start', 'separate-end'
], 'equal positions still yield an ordered contiguous group');
assert.ok(changes.every((change, index) => change.position === index));
assert.equal(changes.find(change => change.task.id === 'separate-start').groupId, undefined);
assert.deepEqual(taskGroupActions(tied, tied[0], new Set([...selection, 'separate-start', 'separate-end'])), []);
tied[0].scheduledDate = '2090-01-01';
assert.deepEqual(taskGroupActions(tied, tied[0], selection), [], 'a group cannot extend across dates');
