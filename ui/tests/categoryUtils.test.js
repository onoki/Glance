import assert from "node:assert/strict";
import { deriveCategories, groupTasksByWeekday } from "../src/utils/categoryUtils.js";
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
assert.equal(grouped.length, 1);
assert.equal(grouped[0].tasks.length, 2);

const now = new Date();
const lastWeek = new Date(getWeekStart(now));
lastWeek.setDate(lastWeek.getDate() - 7);
const pastCategories = deriveCategories([makeTask({ scheduledDate: formatDateKey(lastWeek) })]);
assert.ok(pastCategories.some((cat) => cat.label.startsWith("Week starting")));

const farFuture = new Date(getWeekStart(now));
farFuture.setDate(farFuture.getDate() + 35);
const futureCategories = deriveCategories([makeTask({ scheduledDate: formatDateKey(farFuture) })]);
assert.equal(futureCategories.length, 0);
