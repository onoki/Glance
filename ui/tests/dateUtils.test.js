import assert from "node:assert/strict";
import { formatDateKey, getWeekStart, parseDateKey } from "../src/utils/dateUtils.js";

const value = formatDateKey(new Date("2024-03-05T10:00:00Z"));
assert.equal(value, "2024-03-05");

const date = parseDateKey("2024-03-05");
assert.ok(date instanceof Date);
assert.equal(date?.getFullYear(), 2024);

const weekStart = getWeekStart(new Date("2024-03-07T12:00:00Z"));
assert.equal(weekStart.getDay(), 1);
