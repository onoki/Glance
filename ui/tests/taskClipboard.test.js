import assert from 'node:assert/strict';
import { encodeTaskClipboard, decodeTaskClipboard, clipboardHasImages } from '../src/utils/taskClipboardFormat.js';
import { taskClipboard } from '../src/services/taskClipboard.js';
import { createTaskClipboardAdapter } from '../src/services/taskClipboardAdapter.js';
import { saveCoordinator } from '../src/services/saveCoordinator.js';

const doc = text => ({ type: 'doc', content: [{ type: 'paragraph', content: [{ type: 'text', text }] }] });
const title = doc('Task <one>');
title.content[0].content[0].marks = [{ type: 'bold' }, { type: 'italic' }, { type: 'link', attrs: { href: 'https://example.com/full?x=1&y=2' } }];
const subtask = { type: 'listItem', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'Nested' }] }] };
const content = { type: 'doc', content: [{ type: 'bulletList', content: [{ type: 'listItem', content: [
  { type: 'paragraph', content: [{ type: 'text', text: 'Parent' }, { type: 'hardBreak' }, { type: 'text', text: 'Second line' }] },
  { type: 'bulletList', content: [subtask] }
] }] }] };
const original = { id: 'one', page: 'dashboard:new', ownerPersonId: null, position: 1, title, content, completedAt: null };
const encoded = encodeTaskClipboard([original], 'scope');
assert.deepEqual(decodeTaskClipboard(encoded.html).tasks, [{ title, content }]);
assert.match(encoded.html, /&lt;one&gt;/);
assert.match(encoded.html, /<strong>/);
assert.match(encoded.text, /Parent\nSecond line/);
assert.match(encoded.text, /Nested/);
assert.equal(decodeTaskClipboard('<p>ordinary paste</p>'), null);
assert.equal(decodeTaskClipboard('<p>data-glance-tasks is a code identifier</p>'), null);
assert.throws(() => decodeTaskClipboard('<div data-glance-tasks="%7Bbad"></div>'));
assert.throws(() => encodeTaskClipboard([{ title: doc('Safe'), content: { type: 'doc', content: [{ type: 'script' }] } }], 'scope'));
const imageDoc = { type: 'doc', content: [{ type: 'paragraph', content: [{ type: 'image', attrs: { src: 'http://127.0.0.1:5588/attachments/test.png', width: 30 } }] }] };
const images = decodeTaskClipboard(encodeTaskClipboard([{ title, content: imageDoc }], 'scope').html);
assert.equal(clipboardHasImages(images), true);
assert.equal(images.tasks[0].content.content[0].content[0].attrs.src, '/attachments/test.png');
assert.equal(imageDoc.content[0].content[0].attrs.src, 'http://127.0.0.1:5588/attachments/test.png');

const store = new Map();
const deleted = new Map();
const history = [];
const listeners = new Map();
let writes = 0, rejectClipboard = false, failDelete = null, failAfterDelete = false, mutateWhileWriting = null, counter = 0, editorFocus = 0;
globalThis.window = { location: { origin: 'http://127.0.0.1:5588' }, getSelection: () => ({ removeAllRanges() {} }), alert: message => { throw new Error(message); } };
globalThis.document = { addEventListener: (name, fn) => listeners.set(name, fn), removeEventListener: name => listeners.delete(name) };
Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { clipboard: { write: async () => {
  writes++; if (rejectClipboard) throw new Error('Clipboard denied');
  mutateWhileWriting?.();
} } } });
globalThis.ClipboardItem = class { constructor(data) { this.data = data; } };
globalThis.fetch = async (path, options = {}) => {
  let value;
  if (path === '/api/clipboard-scope') value = { scope: 'scope' };
  else if (path === '/api/dashboard') value = { newTasks: [...store.values()], mainTasks: [] };
  else if (options.method === 'DELETE') {
    const id = path.split('/').at(-1);
    if (id === failDelete) {
      if (failAfterDelete) { deleted.set(id, store.get(id)); store.delete(id); }
      return { ok: false, status: 503, json: async () => ({ message: 'Temporary failure' }) };
    }
    deleted.set(id, store.get(id)); store.delete(id); value = { ok: true };
  } else if (path.endsWith('/restore')) {
    const id = path.split('/').at(-2); store.set(id, deleted.get(id)); deleted.delete(id); value = { updatedAt: 2 };
  } else if (options.method === 'POST' && path === '/api/tasks') {
    const task = { ...JSON.parse(options.body), id: `created-${++counter}`, completedAt: null };
    store.set(task.id, task); value = { taskId: task.id, updatedAt: 1 };
  } else throw new Error(`Unexpected request ${path}`);
  return { ok: true, json: async () => value };
};
const adapter = createTaskClipboardAdapter({ tasks: () => [...store.values()], record: item => history.push(item), refresh: async () => {} });
function register(task) {
  store.set(task.id, task);
  const element = { isConnected: true, compareDocumentPosition: other => task.position < other.position ? 4 : 2, position: task.position };
  const row = { adapter, snapshot: () => task, element: () => element, handle: () => ({ focus() {} }), focusEditor: () => editorFocus++ };
  taskClipboard.register(task.id, row);
  return row;
}
const row = register(structuredClone(original));
register({ ...structuredClone(original), id: 'two', position: 2 });
register({ ...structuredClone(original), id: 'three', position: 3 });
const uninstall = taskClipboard.install(() => row);
taskClipboard.scope = 'scope';
taskClipboard.select('one');
taskClipboard.select('three', { shiftKey: true });
assert.deepEqual([...taskClipboard.selected.value], ['one', 'two', 'three']);
taskClipboard.select('two', { ctrlKey: true });
assert.deepEqual([...taskClipboard.selected.value], ['one', 'three']);

rejectClipboard = true;
await assert.rejects(() => taskClipboard.copy(true), /denied/);
assert.equal(store.size, 3, 'clipboard denial must not delete anything');
assert.equal(taskClipboard.busy.value, false);
rejectClipboard = false;
mutateWhileWriting = () => { row.snapshot().title = doc('New text during clipboard write'); };
await assert.rejects(() => taskClipboard.copy(true), /changed while copying/);
assert.equal(store.size, 3);
mutateWhileWriting = null; row.snapshot().title = structuredClone(title);
const dirty = saveCoordinator.register('blocked', { save: async () => { throw new Error('Offline'); } });
dirty.markDirty();
await assert.rejects(() => taskClipboard.copy(true), /could not be saved/);
assert.equal(store.size, 3, 'failed save must also prevent cut');
saveCoordinator.forget('blocked');

await taskClipboard.copy(true);
assert.equal(store.size, 1);
assert.equal(history.length, 1, 'multi-cut is one undo entry');
await history[0].undo(); assert.equal(store.size, 3);
await history[0].redo(); assert.equal(store.size, 1);
await history[0].undo();

await taskClipboard.paste(decodeTaskClipboard(encoded.html), row);
await taskClipboard.paste(decodeTaskClipboard(encoded.html), row);
assert.equal(store.size, 5, 'repeated paste creates independent IDs');
assert.equal(store.get('created-1').recurrence, null);
assert.deepEqual(store.get('created-1').content, content);
await history.at(-1).undo(); assert.equal(store.has('created-2'), false);
await history.at(-1).redo(); assert.equal(store.has('created-2'), true);
store.get('created-2').title = doc('Changed in another window');
await assert.rejects(() => history.at(-1).undo(), /changed elsewhere/);
assert.equal(store.has('created-2'), true);
await assert.rejects(() => taskClipboard.paste({ ...images, scope: 'another-database' }, row), /another Glance database/);
await taskClipboard.paste(images, row);
assert.equal(store.get('created-3').content.content[0].content[0].attrs.src, 'http://127.0.0.1:5588/attachments/test.png');

taskClipboard.select('one'); taskClipboard.select('three', { ctrlKey: true }); failDelete = 'three';
await assert.rejects(() => taskClipboard.copy(true), /Temporary failure/);
assert.equal(store.has('one'), false); assert.equal(store.has('three'), true);
assert.equal(taskClipboard.busy.value, false);
await history.at(-1).undo(); assert.equal(store.has('one'), true, 'partial cut remains undoable');

taskClipboard.select('one'); failDelete = 'one'; failAfterDelete = true;
await assert.rejects(() => taskClipboard.copy(true), /Temporary failure/);
assert.equal(store.has('one'), false);
await history.at(-1).undo();
assert.equal(store.has('one'), true, 'lost deletion responses retain recoverable undo intent');

let prevented = false;
listeners.get('paste')({ target: { closest: () => null }, clipboardData: { getData: () => '<p>ordinary text</p>' }, preventDefault: () => { prevented = true; } });
assert.equal(prevented, false, 'normal editor paste is untouched');
taskClipboard.select('one');
listeners.get('keydown')({ key: 'Escape', target: { closest: () => null }, preventDefault() {} });
assert.equal(taskClipboard.selected.value.size, 0);
assert.equal(editorFocus, 1);
assert.ok(writes >= 3);
uninstall();
console.log('Whole-task clipboard: structure, selection, safe cut, grouped history, paste, and attachment scope passed');
