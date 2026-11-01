import assert from "node:assert/strict";
import { currentWeekDays, deriveCategories, groupTasksByWeekday, weekDays, planningWeekDays, categoryWeekGroups, categoryPickerDays, relativeWeekStart } from "../src/utils/categoryUtils.js";
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
assert.equal(futureCategories.length, 2);
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

const nextStart = new Date(2026, 9, 5);
const nextGroups = groupTasksByWeekday([], wednesday, nextStart);
assert.equal(nextGroups.length, 7);
assert.deepEqual(nextGroups.map(group => group.dateKey), ['2026-10-05','2026-10-06','2026-10-07','2026-10-08','2026-10-09','2026-10-10','2026-10-11']);
assert.ok(nextGroups.every(group => !group.isPast));
assert.equal(weekDays(wednesday, nextStart).length, 7);
assert.equal(planningWeekDays(wednesday).length, 14);
const emptyNext = deriveCategories([]).find(category => category.label === 'Next week');
assert.equal(categoryWeekGroups(emptyNext).length, 7, 'empty Next week remains available for planning');

assert.deepEqual(categoryPickerDays('this-week', wednesday), currentWeekDays(wednesday));
const nextPicker = categoryPickerDays('next-week', wednesday);
assert.equal(nextPicker.length, 7);
assert.equal(nextPicker[0].dateKey, '2026-10-05');
assert.equal(nextPicker.at(-1).dateKey, '2026-10-11');
assert.ok(nextPicker.every(day => !day.isPast));
assert.deepEqual(categoryPickerDays('two-weeks', wednesday), [], 'no additional day row for the postponement category');
assert.equal(formatDateKey(relativeWeekStart(2, wednesday)), '2026-10-12');
assert.equal(formatDateKey(relativeWeekStart(2, new Date(2026, 11, 31))), '2027-01-11', 'week scheduling crosses years');
const postponed = makeTask({id:'postponed', scheduledDate:'2026-10-12'});
for (const [date, label] of [[wednesday,'In two weeks'],[new Date(2026,9,5),'Next week'],[new Date(2026,9,12),'This week']]) {
  const result = deriveCategories([postponed], date);
  const category = result.find(category => category.tasks.some(task => task.id === postponed.id));
  assert.equal(category.label, label);
  assert.equal(category.id, 'week-2026-10-12', 'rollover relabels without rewriting task dates');
  assert.equal(result.filter(category => category.id === 'week-2026-10-12').length, 1);
}
