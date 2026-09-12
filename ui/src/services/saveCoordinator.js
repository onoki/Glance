const friendlyError = (error) => {
  const status = Number.isFinite(error?.status) ? error.status : null;
  const payload = error?.payload && typeof error.payload === "object" ? error.payload : null;
  const conflict = status === 409;
  const network = !!error?.isNetworkError || status === null;
  const fallback = conflict
    ? "This note changed in another Glance window. Your edits are still here."
    : network
      ? "Glance could not reach the local server. Your edits are still here."
      : "Glance could not save this note. Your edits are still here.";

  return {
    kind: conflict ? "conflict" : network ? "network" : "save",
    status,
    message: error?.message || fallback,
    currentUpdatedAt: Number.isFinite(payload?.currentUpdatedAt)
      ? payload.currentUpdatedAt
      : Number.isFinite(error?.currentUpdatedAt)
        ? error.currentUpdatedAt
        : null
  };
};

const publicState = (entry) => ({
  taskId: entry.taskId,
  generation: entry.generation,
  savedGeneration: entry.savedGeneration,
  dirty: entry.savedGeneration < entry.generation,
  saving: entry.saving,
  pending: entry.pending,
  attached: entry.attached,
  error: entry.error
});

export class SaveCoordinator {
  constructor() {
    this.entries = new Map();
    this.listeners = new Set();
  }

  register(taskId, { save, onStateChange } = {}) {
    if (!taskId || typeof save !== "function") {
      throw new TypeError("A task id and save function are required");
    }

    let entry = this.entries.get(taskId);
    if (entry?.attached) {
      throw new Error(`Task ${taskId} already has an active save registration`);
    }

    if (!entry) {
      entry = {
        taskId,
        generation: 0,
        savedGeneration: 0,
        saving: false,
        pending: 0,
        attached: true,
        error: null,
        save,
        onStateChange,
        queue: Promise.resolve(true),
        token: Symbol(taskId)
      };
      this.entries.set(taskId, entry);
    } else {
      entry.attached = true;
      entry.save = save;
      entry.onStateChange = onStateChange;
      entry.token = Symbol(taskId);
    }

    const token = entry.token;
    this.notify(entry);

    const current = () => this.entries.get(taskId) === entry && entry.token === token;
    return {
      markDirty: () => {
        if (!current()) return publicState(entry);
        entry.generation += 1;
        this.notify(entry);
        return publicState(entry);
      },
      flush: (options = null) => current()
        ? this.enqueue(entry, entry.generation, options)
        : Promise.resolve(false),
      discard: () => {
        if (!current()) return;
        entry.savedGeneration = entry.generation;
        entry.error = null;
        this.notify(entry);
      },
      state: () => publicState(entry),
      unregister: ({ flush = true } = {}) => {
        if (!current()) return Promise.resolve(true);
        const completion = flush
          ? this.enqueue(entry, entry.generation, { reason: "unmount" })
          : Promise.resolve(true);
        entry.attached = false;
        entry.onStateChange = null;
        this.notify(entry);
        return completion.then((ok) => {
          if (ok && entry.savedGeneration >= entry.generation && !entry.attached) {
            this.entries.delete(taskId);
            this.notifyAll();
          }
          return ok;
        });
      }
    };
  }

  enqueue(entry, targetGeneration, options) {
    if (targetGeneration <= entry.savedGeneration && entry.pending === 0 && !entry.saving) {
      return Promise.resolve(true);
    }

    entry.pending += 1;
    this.notify(entry);
    const operation = entry.queue.then(async () => {
      entry.pending = Math.max(0, entry.pending - 1);
      if (targetGeneration <= entry.savedGeneration) {
        this.notify(entry);
        return true;
      }

      entry.saving = true;
      entry.error = null;
      this.notify(entry);
      try {
        const result = await entry.save({ generation: targetGeneration, options });
        if (result === false) {
          throw new Error("The save operation did not complete");
        }
        entry.savedGeneration = Math.max(entry.savedGeneration, targetGeneration);
        entry.error = null;
        return true;
      } catch (error) {
        entry.error = friendlyError(error);
        return false;
      } finally {
        entry.saving = false;
        this.notify(entry);
      }
    });

    // Keep the queue fulfilled after a failed operation so an explicit retry can run.
    entry.queue = operation.catch(() => false);
    return operation;
  }

  async flushAll(options = null) {
    const entries = Array.from(this.entries.values());
    const results = await Promise.all(entries.map(async (entry) => ({
      entry,
      ok: await this.enqueue(entry, entry.generation, options || { reason: "flush-all" })
    })));
    const failures = results
      .filter(({ entry, ok }) => !ok || entry.savedGeneration < entry.generation)
      .map(({ entry }) => ({ taskId: entry.taskId, error: entry.error }));

    for (const { entry, ok } of results) {
      if (ok && !entry.attached && entry.savedGeneration >= entry.generation) {
        this.entries.delete(entry.taskId);
      }
    }
    this.notifyAll();
    return { ok: failures.length === 0, failures };
  }

  getSummary() {
    const states = Array.from(this.entries.values(), publicState);
    return {
      dirty: states.filter((state) => state.dirty).length,
      saving: states.filter((state) => state.saving || state.pending > 0).length,
      failures: states.filter((state) => !!state.error),
      tasks: states
    };
  }

  subscribe(listener) {
    if (typeof listener !== "function") {
      throw new TypeError("A listener function is required");
    }
    this.listeners.add(listener);
    listener(this.getSummary());
    return () => this.listeners.delete(listener);
  }

  notify(entry) {
    entry.onStateChange?.(publicState(entry));
    this.notifyAll();
  }

  notifyAll() {
    if (this.listeners.size === 0) return;
    const summary = this.getSummary();
    for (const listener of this.listeners) listener(summary);
  }
}

export const saveCoordinator = new SaveCoordinator();
export const flushAllSaves = (options) => saveCoordinator.flushAll(options);
export const getSaveSummary = () => saveCoordinator.getSummary();
export const subscribeToSaveSummary = (listener) => saveCoordinator.subscribe(listener);
