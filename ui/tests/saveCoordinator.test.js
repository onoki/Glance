import assert from "node:assert/strict";
import { SaveCoordinator } from "../src/services/saveCoordinator.js";

const deferred = () => {
  let resolve;
  let reject;
  const promise = new Promise((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
};

const tick = () => new Promise((resolve) => setTimeout(resolve, 0));

{
  const coordinator = new SaveCoordinator();
  const saves = [];
  const first = deferred();
  const second = deferred();
  const handle = coordinator.register("task-1", {
    save: ({ generation }) => {
      saves.push(generation);
      return saves.length === 1 ? first.promise : second.promise;
    }
  });

  handle.markDirty();
  const firstFlush = handle.flush();
  await tick();
  assert.deepEqual(saves, [1], "the first generation starts saving");

  handle.markDirty();
  const secondFlush = handle.flush();
  await tick();
  assert.deepEqual(saves, [1], "saves for one task are serialized");
  assert.equal(handle.state().dirty, true, "edits made during a save remain dirty");

  first.resolve({ updatedAt: 2 });
  assert.equal(await firstFlush, true);
  await tick();
  assert.deepEqual(saves, [1, 2], "the newer generation saves after the first finishes");
  assert.equal(handle.state().dirty, true, "the first response cannot clear a newer edit");
  second.resolve({ updatedAt: 3 });
  assert.equal(await secondFlush, true);
  assert.equal(handle.state().dirty, false);
}

{
  const coordinator = new SaveCoordinator();
  let attempts = 0;
  const states = [];
  const handle = coordinator.register("conflict", {
    save: async () => {
      attempts += 1;
      if (attempts === 1) {
        const error = new Error("Changed in another window");
        error.status = 409;
        error.payload = { currentUpdatedAt: 42 };
        throw error;
      }
      return { updatedAt: 43 };
    },
    onStateChange: (state) => states.push(state)
  });

  handle.markDirty();
  assert.equal(await handle.flush(), false);
  assert.equal(handle.state().dirty, true, "a conflict never clears local dirty state");
  assert.equal(handle.state().error.kind, "conflict");
  assert.equal(handle.state().error.currentUpdatedAt, 42);
  assert.equal(await handle.flush(), true, "failed saves can be retried explicitly");
  assert.equal(handle.state().dirty, false);
  assert.ok(states.some((state) => state.saving), "in-flight state is observable");
}

{
  const coordinator = new SaveCoordinator();
  const finalSave = deferred();
  const handle = coordinator.register("unmount", { save: () => finalSave.promise });
  handle.markDirty();
  const unregistering = handle.unregister();
  await tick();
  assert.equal(coordinator.getSummary().dirty, 1, "an unmounted dirty task remains tracked");
  assert.equal(coordinator.getSummary().saving, 1, "its final save remains in-flight");
  finalSave.resolve({ updatedAt: 2 });
  assert.equal(await unregistering, true);
  assert.equal(coordinator.getSummary().tasks.length, 0, "a successful detached save is released");
}

{
  const coordinator = new SaveCoordinator();
  let online = false;
  const handle = coordinator.register("network", {
    save: async () => {
      if (!online) {
        const error = new Error("Server unavailable");
        error.isNetworkError = true;
        throw error;
      }
      return { updatedAt: 2 };
    }
  });
  handle.markDirty();
  assert.equal(await handle.unregister(), false);
  assert.equal(coordinator.getSummary().failures[0].error.kind, "network");
  online = true;
  const result = await coordinator.flushAll();
  assert.equal(result.ok, true, "flushAll retries and awaits detached failed saves");
  assert.equal(coordinator.getSummary().tasks.length, 0);
}

