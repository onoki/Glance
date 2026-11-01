import { normalizeTitle, normalizeContent } from './taskUtils.js';

const canonical = value => JSON.stringify(value, (_, item) => item && typeof item === 'object' && !Array.isArray(item)
  ? Object.fromEntries(Object.keys(item).sort().map(key => [key, item[key]])) : item);

// Compare the fields recorded by this history entry, including rich content and
// intentional blank lines. Revisions change on restore; document checks do not.
const historyFields = ['title', 'content', 'page', 'position', 'scheduledDate', 'recurrence', 'ownerPersonId', 'completedAt', 'isPrivate'];
export const matchesTaskSnapshot = (task, snapshot) => !!task && !!snapshot && historyFields
  .filter(key => Object.hasOwn(snapshot, key))
  .every(key => canonical(key === 'title' ? normalizeTitle(task[key]) : key === 'content' ? normalizeContent(task[key]) : key === 'isPrivate' ? !!task[key] : task[key] ?? null)
    === canonical(key === 'title' ? normalizeTitle(snapshot[key]) : key === 'content' ? normalizeContent(snapshot[key]) : key === 'isPrivate' ? !!snapshot[key] : snapshot[key] ?? null));

export const taskRevision = task => {
  if (!Number.isFinite(task?.updatedAt)) throw new Error('The note revision is unavailable. Reload before trying again.');
  return task.updatedAt;
};

// Restore missing rows, write retained destinations, then retire copies. Fetch again
// before every write and revision-guard it. Accept the target snapshot on retry
// (including a lost response), but never accept arbitrary newer text.
export async function applyTaskHistory(diffs, direction, api) {
  const targetKey = direction === 'undo' ? 'before' : 'after';
  const expectedKey = direction === 'undo' ? 'after' : 'before';
  const current = async diff => (await api.fetchTasks()).find(task => task.id === api.resolveId(diff));
  const check = (row, diff) => {
    if (row && !matchesTaskSnapshot(row, diff[expectedKey]) && !matchesTaskSnapshot(row, diff[targetKey])) {
      throw new Error('This note changed elsewhere. Undo or redo was stopped to preserve the newer content.');
    }
    if (!row && diff[targetKey] && diff[expectedKey]) {
      throw new Error('This note is no longer available. Undo or redo was stopped without replacing it.');
    }
  };
  // Validate the entire operation before making even its first change.
  for (const diff of diffs) check(await current(diff), diff);
  for (const diff of diffs.filter(diff => diff[targetKey])) {
    let row = await current(diff);
    check(row, diff);
    if (!row) {
      await api.restoreTask(api.resolveId(diff));
      row = await current(diff);
      check(row, diff);
      if (!row) throw new Error('The restored note could not be read. Undo or redo remains available to retry.');
    }
  }
  // Restore every missing row before shrinking any currently merged document.
  // In particular, Undo of a merge must not shorten its sole surviving copy
  // while restoration of the lower task can still fail.
  for (const diff of diffs.filter(diff => diff[targetKey])) {
    const row = await current(diff);
    check(row, diff);
    if (matchesTaskSnapshot(row, diff[targetKey])) continue;
    const snapshot = diff[targetKey];
    await api.updateTask(row.id, {
      baseUpdatedAt: taskRevision(row), title: normalizeTitle(snapshot.title), content: normalizeContent(snapshot.content),
      page: snapshot.page, position: snapshot.position, scheduledDate: snapshot.scheduledDate ?? null,
      recurrence: snapshot.recurrence ?? null, isPrivate: snapshot.isPrivate
    });
  }
  for (const diff of diffs.filter(diff => diff[targetKey] === null)) {
    for (const retained of diffs.filter(item => item[targetKey])) {
      if (!matchesTaskSnapshot(await current(retained), retained[targetKey])) {
        throw new Error('The destination note changed before removal. Both notes have been kept.');
      }
    }
    const row = await current(diff);
    check(row, diff);
    if (!row) continue;
    await api.deleteTask(row.id, taskRevision(row));
    api.forget?.(row.id);
  }
  return true;
}
