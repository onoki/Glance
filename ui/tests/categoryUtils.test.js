import assert from "node:assert/strict";
import { currentWeekDays, deriveCategories, groupTasksByWeekday } from "../src/utils/categoryUtils.js";
import { formatDateKey, getWeekStart } from "../src/utils/dateUtils.js";

const makeTask = (overrides = {}) => ({
  id: overrides.id || "task-1",
  recurrence: overrides.recurrence || null,
  scheduledDate: overrides.scheduledDate ?? null,
  ...overrides
});

const categories = deriveCategories([
  makeTask({ id: "t1" }),
  makeTask({ id: "t2", recurrence: { type: "notes" } })
]);
assert.ok(categories.find((cat) => cat.id === "uncategorized"));
assert.ok(categories.find((cat) => cat.id === "notes"));

const today = new Date();
const dayKey = formatDateKey(today);
const weekCategories = deriveCategories([
  makeTask({ id: "t1", scheduledDate: dayKey }),
  makeTask({ id: "t2", scheduledDate: dayKey })
]);
const thisWeek = weekCategories.find((cat) => cat.label === "This week");
assert.ok(thisWeek);
const grouped = groupTasksByWeekday(thisWeek.tasks);
assert.equal(grouped.length, 8 - (((today.getDay() + 6) % 7) + 1));
assert.equal(grouped[0].tasks.length, 2);

const now = new Date();
const lastWeek = new Date(getWeekStart(now));
lastWeek.setDate(lastWeek.getDate() - 7);
const pastCategories = deriveCategories([makeTask({ scheduledDate: formatDateKey(lastWeek) })]);
assert.ok(pastCategories.some((cat) => cat.label.startsWith("Week starting")));

const farFuture = new Date(getWeekStart(now));
farFuture.setDate(farFuture.getDate() + 35);
const futureCategories = deriveCategories([makeTask({ scheduledDate: formatDateKey(farFuture) })]);
assert.equal(futureCategories.length, 1);
assert.equal(futureCategories[0].label, "This week");
assert.deepEqual(futureCategories[0].tasks, []);

// Wednesday: retain overdue Monday tasks, hide empty Tuesday, expose Wed-Sun.
const wednesday = new Date(2026, 8, 30);
const overdue = makeTask({ id: "overdue", scheduledDate: "2026-09-28" });
const days = groupTasksByWeekday([overdue], wednesday);
assert.deepEqual(days.map(day => day.label), ["Monday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"]);
assert.equal(days[0].isPast, true);
assert.equal(days[0].tasks[0].scheduledDate, "2026-09-28");
assert.equal(days.at(-1).dateKey, "2026-10-04");
assert.deepEqual(groupTasksByWeekday([], new Date(2026, 9, 4)).map(day => day.label), ["Sunday"]);
assert.deepEqual(currentWeekDays(wednesday).map(day => day.label), ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"], "the compact Move picker keeps abbreviated labels");
assert.equal(groupTasksByWeekday([], new Date(2026, 9, 5)).length, 7);
