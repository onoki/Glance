import assert from 'node:assert/strict';
import { installDayRollover, millisecondsUntilNextDay } from '../src/utils/dayRollover.js';
const hostHandlers = new Map(), docHandlers = new Map(), timers = new Map(), errors = [];
let counter = 0, checks = 0, resolveCheck, fail = false;
const host = { addEventListener: (key, fn) => hostHandlers.set(key, fn), removeEventListener: key => hostHandlers.delete(key) };
const doc = { visibilityState: 'visible', addEventListener: (key, fn) => docHandlers.set(key, fn), removeEventListener: key => docHandlers.delete(key) };
assert.equal(millisecondsUntilNextDay(new Date(2026, 9, 31, 23, 59, 59, 990)), 30);
const dispose = installDayRollover(() => {
  checks++;
  if (fail) throw new Error('Save not ready');
  return new Promise(resolve => { resolveCheck = resolve; });
}, { host, doc, now: () => new Date(2026, 9, 31, 23, 59, 59, 990),
  setTimer: (fn, delay) => { timers.set(++counter, { fn, delay }); return counter; }, clearTimer: id => timers.delete(id),
  report: error => errors.push(error.message) });
assert.equal([...timers.values()][0].delay, 30, 'midnight is scheduled without a minute delay');
const pending = hostHandlers.get('focus')();
docHandlers.get('visibilitychange')();
await Promise.resolve(); assert.equal(checks, 1, 'wake/focus events share one in-flight cleanup');
resolveCheck(); await pending;
assert.equal(timers.size, 1);
doc.visibilityState = 'hidden'; docHandlers.get('visibilitychange')();
await Promise.resolve(); assert.equal(checks, 1);
fail = true;
await [...timers.values()][0].fn();
assert.deepEqual(errors, ['Save not ready']);
assert.equal(timers.size, 1, 'failed cleanup remains scheduled for retry');
dispose(); assert.equal(timers.size, 0); assert.equal(hostHandlers.size, 0); assert.equal(docHandlers.size, 0);
console.log('Day rollover: midnight, wake, event coalescing, retries and cleanup passed');

// Exercise the real day-change coordinator, including a failed pending save
// and a failed archive response. Neither may advance the day/retry checkpoint.
const { useDashboardData } = await import('../src/composables/useDashboardData.js');
const { saveCoordinator } = await import('../src/services/saveCoordinator.js');
const { ref } = await import('vue');
const originalFetch = globalThis.fetch;
const requests = [];
let archiveFails = true;
globalThis.fetch = async path => {
  requests.push(path);
  if (path === '/api/history/archive-previous-days' && archiveFails) return { ok: false, status: 503, json: async () => ({ message: 'Retry archive' }) };
  return { ok: true, json: async () => path === '/api/dashboard' ? {
    newTasks: [{ id: 'open', page: 'dashboard:new', title: { type: 'doc', content: [{ type: 'paragraph' }] }, content: { type: 'doc', content: [{ type: 'paragraph' }] }, position: 0 }], mainTasks: []
  } : {} };
};
try {
  const dashboard = useDashboardData({ activeTab: ref('History'), loadHistory: async () => requests.push('history'), loadWarnings: async () => requests.push('warnings') });
  dashboard.currentDayKey.value = '2000-01-01';
  const blocked = saveCoordinator.register('overnight-pending', { save: async () => { throw new Error('Unsaved text'); } });
  blocked.markDirty();
  await dashboard.handleDayTick();
  assert.deepEqual(requests, [], 'pending text must settle before automatic archiving');
  assert.equal(dashboard.currentDayKey.value, '2000-01-01');
  saveCoordinator.forget('overnight-pending');
  await assert.rejects(dashboard.handleDayTick(), /Retry archive/);
  assert.equal(dashboard.currentDayKey.value, '2000-01-01');
  archiveFails = false; requests.length = 0;
  await dashboard.handleDayTick();
  assert.deepEqual(requests, ['/api/history/archive-previous-days', '/api/recurrence/run', '/api/maintenance/daily', '/api/dashboard', 'history', 'warnings']);
  assert.notEqual(dashboard.currentDayKey.value, '2000-01-01');
  requests.length = 0; await dashboard.handleDayTick();
  assert.deepEqual(requests, [], 'same-day focus does not rearchive today’s completed notes');
} finally { globalThis.fetch = originalFetch; saveCoordinator.forget('overnight-pending'); }
console.log('Day rollover coordinator: guarded saves, failed archive retry and History refresh passed');
