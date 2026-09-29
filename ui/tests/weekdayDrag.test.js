import assert from "node:assert/strict";
import { useDashboardDrag } from "../src/composables/useDashboardDrag.js";
import { currentWeekDays } from "../src/utils/categoryUtils.js";
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
