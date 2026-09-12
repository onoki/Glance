import assert from "node:assert/strict";
import { resolveSearchDestination } from "../src/utils/searchNavigation.js";

assert.deepEqual(
  resolveSearchDestination({ id: "dashboard", page: "dashboard:main", completedAt: null }),
  { tab: "Dashboard", taskId: "dashboard" }
);
assert.deepEqual(
  resolveSearchDestination({ id: "person", page: "people:main", ownerPersonId: "p1", ownerPersonName: "Alice", completedAt: null }),
  { tab: "People", taskId: "person", personId: "p1", personName: "Alice" }
);
assert.deepEqual(
  resolveSearchDestination({ id: "done", page: "people:main", ownerPersonId: "p1", completedAt: 123 }),
  { tab: "History", taskId: "done" }
);
assert.equal(resolveSearchDestination({ id: "unknown", page: "other", completedAt: null }), null);
assert.equal(resolveSearchDestination(null), null);

