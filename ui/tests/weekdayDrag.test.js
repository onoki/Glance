import assert from "node:assert/strict";
import { useDashboardDrag } from "../src/composables/useDashboardDrag.js";
import { currentWeekDays } from "../src/utils/categoryUtils.js";
import { ref } from "vue";
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
