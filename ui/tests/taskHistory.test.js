import assert from 'node:assert/strict';
import { applyTaskHistory, matchesTaskSnapshot } from '../src/utils/taskHistory.js';

const clone = value => JSON.parse(JSON.stringify(value));
const doc = text => ({ type: 'doc', content: [{ type: 'paragraph', content: [{ type: 'text', text }] }] });
const snapshot = text => ({ page: 'dashboard:new', title: doc(text), content: { type: 'doc', content: [{ type: 'paragraph' }] }, position: 0, scheduledDate: null, recurrence: null });
const before = snapshot('Full source');
const after = snapshot('Full ');
const copy = snapshot('source');
const diffs = [{ id: 'source', before, after }, { id: 'copy', before: null, after: copy }];
let cases = 0;

for (const mode of ['update-failure', 'lost-update-response', 'delete-failure', 'lost-delete-response', 'external-source', 'external-copy', 'destination-changed']) {
  const rows = [{ ...clone(after), id: 'source', updatedAt: 1 }, { ...clone(copy), id: 'copy', updatedAt: 1 }];
  let failed = false;
  const api = {
    fetchTasks: async () => clone(rows), resolveId: diff => diff.id,
    updateTask: async (id, payload) => {
      if (mode === 'update-failure' && !failed) { failed = true; throw new Error('Save failed'); }
      const row = rows.find(row => row.id === id);
      assert.equal(payload.baseUpdatedAt, row.updatedAt);
      Object.assign(row, clone(payload), { updatedAt: row.updatedAt + 1 });
      if (mode === 'lost-update-response' && !failed) { failed = true; throw new Error('Response lost'); }
      if (mode === 'destination-changed' && !failed) { failed = true; row.title = doc('NEW destination text'); row.updatedAt++; }
    },
    deleteTask: async (id, revision) => {
      if (mode === 'delete-failure' && !failed) { failed = true; throw new Error('Delete failed'); }
      const index = rows.findIndex(row => row.id === id);
      assert.equal(revision, rows[index].updatedAt);
      rows.splice(index, 1);
      if (mode === 'lost-delete-response' && !failed) { failed = true; throw new Error('Response lost'); }
    }
  };
  if (mode.startsWith('external-')) { rows[mode === 'external-source' ? 0 : 1].title = doc('NEW external text'); rows[mode === 'external-source' ? 0 : 1].updatedAt++; }
  await assert.rejects(applyTaskHistory(diffs, 'undo', api));
  if (mode.startsWith('external-')) {
    assert.equal(rows.length, 2);
    assert.ok(rows.some(row => JSON.stringify(row.title).includes('NEW external text')));
    assert.equal(failed, false, 'preflight conflicts cause no writes');
  } else if (mode === 'destination-changed') {
    assert.equal(rows.length, 2, 'retain copy if destination changes before removal');
    assert.match(JSON.stringify(rows[0].title), /NEW destination text/);
    await assert.rejects(applyTaskHistory(diffs, 'undo', api));
  } else {
    if (mode !== 'lost-delete-response') assert.equal(rows.length, 2, 'failed history must retain the source copy');
    await applyTaskHistory(diffs, 'undo', api);
    assert.equal(rows.length, 1);
    assert.ok(matchesTaskSnapshot(rows[0], before), 'partial retries reach the complete target');
  }
  cases++;
}

for (const mode of ['restore-failure', 'lost-restore-response', 'restored-newer-content', 'second-write-failure']) {
  const lower = snapshot('Lower');
  const upper = snapshot('Upper');
  const merged = snapshot('UpperLower');
  const mergeDiffs = [{ id: 'upper', before: upper, after: merged }, { id: 'lower', before: lower, after: null }];
  const rows = [{ ...clone(merged), id: 'upper', updatedAt: 2 }];
  let failed = false;
  const api = {
    fetchTasks: async () => clone(rows), resolveId: diff => diff.id,
    restoreTask: async id => {
      if (mode === 'restore-failure' && !failed) { failed = true; throw new Error('Restore failed'); }
      rows.push({ ...clone(lower), id, updatedAt: 3 });
      if (mode === 'lost-restore-response' && !failed) { failed = true; throw new Error('Response lost'); }
      if (mode === 'restored-newer-content') rows.at(-1).title = doc('NEW restored text');
    },
    updateTask: async (id, payload) => {
      if (mode === 'second-write-failure' && !failed) { failed = true; throw new Error('Save failed'); }
      const row = rows.find(row => row.id === id);
      assert.equal(payload.baseUpdatedAt, row.updatedAt);
      Object.assign(row, clone(payload), { updatedAt: row.updatedAt + 1 });
    }
  };
  await assert.rejects(applyTaskHistory(mergeDiffs, 'undo', api));
  if (mode === 'restore-failure' || mode === 'second-write-failure') {
    assert.ok(matchesTaskSnapshot(rows.find(row => row.id === 'upper'), merged), 'merged text remains visible until restoration and writes succeed');
  }
  if (mode === 'restored-newer-content') {
    assert.match(JSON.stringify(rows.at(-1).title), /NEW restored text/);
    await assert.rejects(applyTaskHistory(mergeDiffs, 'undo', api));
  } else {
    await applyTaskHistory(mergeDiffs, 'undo', api);
    assert.ok(matchesTaskSnapshot(rows.find(row => row.id === 'upper'), upper));
    assert.ok(matchesTaskSnapshot(rows.find(row => row.id === 'lower'), lower));
  }
  cases++;
}
console.log(`Task history: ${cases} partial failures, retries, lost responses and external edits`);
