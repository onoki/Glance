import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { compileScript, parse } from '@vue/compiler-sfc';
import { createRenderer, getCurrentInstance, h, nextTick, ref } from 'vue';
import { Schema } from 'prosemirror-model';
import { EditorState, TextSelection } from 'prosemirror-state';
import { useDashboardData } from '../src/composables/useDashboardData.js';
import { usePeopleTasks } from '../src/composables/usePeopleTasks.js';
import { saveCoordinator } from '../src/services/saveCoordinator.js';
import { formatDateKey, getWeekStart } from '../src/utils/dateUtils.js';
import { splitTitleDocAtOffsets } from '../src/utils/titleSplitUtils.js';

// Real TaskItem lifecycle, save ownership, key handler and list models; only
// the browser DOM and transport are replaced. Include strict revision checks.
const url = new URL('../src/components/TaskItem.vue', import.meta.url);
let script = compileScript(parse(await readFile(url, 'utf8')).descriptor, { id: 'title-enter-safety' }).content;
script = script.replace(/import (\w+) from "[^"\n]+\.vue";/g, 'const $1 = {};')
  .replace(/from "([^"]+)"/g, (_, s) => `from ${JSON.stringify(s.startsWith('.') ? new URL(s, url).href : import.meta.resolve(s))}`);
const { default: TaskItem } = await import(`data:text/javascript;base64,${Buffer.from(script).toString('base64')}`);
const node = type => ({ type, children: [], parent: null });
const renderer = createRenderer({
  createElement: node, createText: () => node('text'), createComment: () => node('comment'),
  setText() {}, setElementText() {}, patchProp() {}, parentNode: n => n.parent,
  nextSibling: n => n.parent?.children[n.parent.children.indexOf(n) + 1] || null,
  insert(n, p, anchor) {
    if (n.parent) n.parent.children.splice(n.parent.children.indexOf(n), 1);
    n.parent = p;
    const index = anchor ? p.children.indexOf(anchor) : -1;
    p.children.splice(index < 0 ? p.children.length : index, 0, n);
  },
  remove(n) { if (n.parent) n.parent.children.splice(n.parent.children.indexOf(n), 1); n.parent = null; }
});
const schema = new Schema({ nodes: {
  doc: { content: 'paragraph+' }, paragraph: { content: 'inline*' },
  text: { group: 'inline' }, hardBreak: { group: 'inline', inline: true },
  image: { group: 'inline', inline: true, atom: true, attrs: { src: {} } }
}, marks: { bold: {}, link: { attrs: { href: {} } } } });
const empty = { type: 'doc', content: [{ type: 'paragraph' }] };
const title = { type: 'doc', content: [{ type: 'paragraph', attrs: { glanceId: 'keep-title-id', statusInputAtUtc: '2026-10-08T09:00:00Z' }, content: [
  { type: 'text', text: 'KEEP title', marks: [{ type: 'bold' }] },
  { type: 'hardBreak' }, { type: 'text', text: 'KEEP link', marks: [{ type: 'link', attrs: { href: 'https://example.com' } }] }
] }] };
const subcontent = { type: 'doc', content: [{ type: 'bulletList', content: [
  { type: 'listItem', content: [{ type: 'paragraph' }] },
  { type: 'listItem', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'KEEP parent' }] },
    { type: 'bulletList', content: [{ type: 'listItem', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'KEEP nested' }] }] }] }] },
  { type: 'listItem', content: [{ type: 'paragraph', content: [{ type: 'image', attrs: { src: '/keep.png' } }] }] },
  { type: 'listItem', content: [{ type: 'paragraph' }] }
] }] };
const clone = value => JSON.parse(JSON.stringify(value));
const settle = async () => { for (let i = 0; i < 20; i++) { await nextTick(); await new Promise(resolve => setImmediate(resolve)); } };
const globals = Object.fromEntries(['window', 'document', 'ResizeObserver', 'requestAnimationFrame', 'cancelAnimationFrame', 'fetch'].map(k => [k, globalThis[k]]));
const alerts = [];
globalThis.window = { addEventListener() {}, removeEventListener() {}, alert: text => alerts.push(text) };
globalThis.document = { addEventListener() {}, removeEventListener() {}, querySelector: () => null, querySelectorAll: () => [] };
globalThis.ResizeObserver = class { observe() {} disconnect() {} };
globalThis.requestAnimationFrame = callback => { callback(); return 0; };
globalThis.cancelAnimationFrame = () => {};
const noop = () => {};
const bindings = Object.fromEntries(['onComplete', 'onTabToPrevious', 'onSplitToNewTask', 'onFocusPrevTaskFromTitle', 'onFocusNextTaskFromContent', 'onDelete'].map(k => [k, noop]));
let cases = 0;
try {
  // Today, next week, old weeks, unscheduled categories and People, first and
  // second rows, zero/negative positions, rich subcontent and pending edits.
  for (const view of ['next-week', 'this-week', 'past-week', 'notes', 'no-date', 'uncategorized', 'new', 'People']) {
    for (const first of [true, false]) {
      const page = view === 'People' ? 'people:main' : view === 'new' ? 'dashboard:new' : 'dashboard:main';
      const date = getWeekStart(new Date());
      date.setDate(date.getDate() + (view === 'next-week' ? 9 : view === 'past-week' ? -5 : 2));
      const scheduledDate = view.includes('week') ? formatDateKey(date) : view === 'no-date' ? 'no-date' : null;
      const categoryId = view.includes('week') ? `week-${formatDateKey(getWeekStart(date))}` : view;
      const source = { id: 'source', page, ownerPersonId: view === 'People' ? 'person' : null,
        title: clone(title), content: first ? clone(empty) : clone(subcontent), position: first ? -1 : 0,
        updatedAt: 1, completedAt: null, scheduledDate, recurrence: view === 'notes' ? { type: 'notes' } : null };
      const rows = [source, { ...clone(source), id: 'tail', position: first ? 1 : 4 }];
      if (!first) rows.unshift({ ...clone(source), id: 'previous', position: -2 });
      if (view === 'this-week' || view === 'next-week') {
        const otherDay = getWeekStart(date);
        rows.push({ ...clone(source), id: 'other-day', scheduledDate: formatDateKey(otherDay), position: 100 });
      }
      const initialCount = rows.length;
      const removed = new Map();
      let releaseCreate, failCreate = false, failSave = false, writes = 0, sequence = 0;
      const update = async (id, payload) => {
        if (failSave) throw new Error('Simulated save failure');
        const row = rows.find(r => r.id === id);
        assert.equal(payload.baseUpdatedAt, row.updatedAt);
        Object.assign(row, clone(payload), { updatedAt: row.updatedAt + 1 });
        writes++;
        return { updatedAt: row.updatedAt };
      };
      const create = async payload => {
        await new Promise(resolve => { releaseCreate = resolve; });
        if (failCreate) throw new Error('Simulated create failure');
        const taskId = `created-${++sequence}`;
        rows.push({ ...clone(payload), id: taskId, updatedAt: 1, completedAt: null });
        return { taskId, updatedAt: 1 };
      };
      const remove = async id => {
        const index = rows.findIndex(row => row.id === id);
        removed.set(id, clone(rows[index])); rows.splice(index, 1);
        return { ok: true };
      };
      const restore = async id => {
        const row = removed.get(id); row.updatedAt++; rows.push(row); removed.delete(id);
        return { updatedAt: row.updatedAt };
      };
      const fetchTasks = async () => ({ tasks: clone(rows).sort((a, b) => a.position - b.position) });
      globalThis.fetch = async (path, request) => {
        try {
          const result = path === '/api/dashboard' ? { newTasks: clone(rows.filter(r => r.page === 'dashboard:new')), mainTasks: clone(rows.filter(r => r.page === 'dashboard:main')) }
            : path === '/api/tasks' ? await create(JSON.parse(request.body))
              : path.endsWith('/restore') ? await restore(path.split('/')[3])
                : request.method === 'DELETE' ? await remove(path.split('/')[3].split('?')[0])
                  : await update(path.split('/')[3], JSON.parse(request.body));
          if (path === '/api/dashboard') { result.newTasks.sort((a,b) => a.position-b.position); result.mainTasks.sort((a,b) => a.position-b.position); }
          return { ok: true, json: async () => result };
        } catch (error) { return { ok: false, status: 500, json: async () => ({ message: error.message }) }; }
      };
      const model = view === 'People' ? usePeopleTasks({ selectedPersonId: ref('person'), api: {
        fetchPersonTasks: fetchTasks, updateTask: update, createTask: create, deleteTask: remove, restoreTask: restore
      } }) : useDashboardData();
      const tasks = view === 'People' ? model.tasks : page === 'dashboard:new' ? model.newTasks : model.mainTasks;
      await (view === 'People' ? model.loadTasks() : model.loadDashboard());
      const states = new Map();
      TaskItem.render = function () { states.set(getCurrentInstance().props.task.id, getCurrentInstance().setupState); return h('task'); };
      const app = renderer.createApp({ render: () => h('list', tasks.value.map(task => h(TaskItem, {
        ...bindings, key: task.id, task, categoryId, onSave: model.saveTask, onDirty: model.handleDirtyChange,
        onCreateBelow: model.createTaskBelow, onSplitTitleToNewTask: model.splitTitleToNewTask,
        undoSignal: model.undoSignal.value
      }))) });
      app.mount(node('root'));
      const pressEnter = (from = 1, to = from, schemaDefaults = false) => {
        const state = states.get('source');
        const doc = schema.nodeFromJSON(state.title);
        const editor = { state: EditorState.create({ doc, selection: TextSelection.create(doc, from, to) }), getJSON: () => {
          const serialized = clone(state.title);
          if (schemaDefaults) serialized.content[0].attrs = { glanceId: null, statusInputAtUtc: null };
          return serialized;
        } };
        editor.commands = { setContent: value => {
          state.title = clone(value);
          if (schemaDefaults) state.title.content[0].attrs = { glanceId: null, statusInputAtUtc: null, ...state.title.content[0].attrs };
        } };
        let prevented = false;
        assert.equal(state.handleTitleKeydown({ key: 'Enter', preventDefault() { prevented = true; } }, editor), true);
        return prevented;
      };
      try {
        const original = clone(source);
        assert.equal(pressEnter(), true, 'handled Enter prevents native paragraph splitting');
        await settle();
        assert.ok(releaseCreate, 'Enter reaches creation');
        assert.deepEqual(source.title, original.title, `${view}: source title remains saved while creation is pending`);
        assert.deepEqual(source.content, original.content, `${view}: subcontent remains saved while creation is pending`);
        assert.deepEqual(states.get('source').title, original.title, 'source stays visible');
        pressEnter(); await settle();
        releaseCreate(); await settle();
        assert.equal(writes, 0, 'inserting above never rewrites the source');
        const inserted = rows.find(r => r.id.startsWith('created-'));
        assert.ok(inserted.position < source.position && (first || inserted.position > -2), 'empty task inserted immediately above, including zero positions');
        assert.equal(inserted.scheduledDate ?? null, scheduledDate);
        assert.deepEqual(inserted.recurrence ?? null, original.recurrence);
        assert.deepEqual(source, original, 'source identity, text, metadata and date are unchanged');
        assert.deepEqual(model.focusTaskId.value, { taskId: 'source', selection: { from: 1, to: 1 } });
        await model.undo(); await settle();
        assert.equal(rows.length, initialCount); assert.deepEqual(source, original);
        await model.redo(); await settle();
        assert.equal(rows.length, initialCount + 1); assert.deepEqual(source, original);

        // Failure after a pending title edit: keep that edit and all subcontent.
        states.get('source').title = clone(title);
        states.get('source').title.content[0].content[0].text += ' edited';
        states.get('source').scheduleSave();
        failCreate = true;
        const alertCount = alerts.length;
        pressEnter(); await settle(); releaseCreate(); await settle();
        assert.equal(alerts.length, alertCount + 1, 'creation failure is reported, not an unhandled rejection');
        assert.equal(source.title.content[0].content[0].text, 'KEEP title edited');
        assert.deepEqual(source.content, original.content);
        assert.equal((await saveCoordinator.flushAll()).ok, true);

        // Selected and middle-title splits must not clear the source before
        // the copy exists, and create errors must retain rich subcontent.
        for (const [from, to] of [[5, 5], [1, 5], [5, 8]]) {
          const beforeSplit = clone(source);
          const errorsBeforeSplit = alerts.length;
          pressEnter(from, to); await settle();
          assert.deepEqual(source, beforeSplit);
          assert.deepEqual(states.get('source').title, beforeSplit.title);
          releaseCreate(); await settle();
          assert.equal(alerts.length, errorsBeforeSplit + 1);
          assert.deepEqual(source, beforeSplit, 'failed split creation keeps title, nested bullets, images and blanks');
        }
        failCreate = false;
        for (const [from, to] of [[5, 5], [1, 5], [5, 8]]) {
          const beforeSplit = clone(source);
          const expected = splitTitleDocAtOffsets(beforeSplit.title, from - 1, to - 1);
          pressEnter(from, to); await settle();
          assert.deepEqual(source, beforeSplit, 'delayed copy keeps original intact');
          releaseCreate(); await settle();
          assert.deepEqual(source.title, expected.before);
          assert.deepEqual(source.content, empty);
          const copy = rows.find(row => row.id === `created-${sequence}`);
          assert.equal(copy.position, (source.position + rows.find(row => row.id === 'tail').position) / 2, 'zero is a valid adjacent position, never a timestamp fallback');
          assert.deepEqual(copy.title, expected.after);
          assert.deepEqual(copy.content, beforeSplit.content);
          assert.equal(copy.scheduledDate ?? null, scheduledDate);
          await model.undo(); await settle();
          assert.deepEqual(source.title, beforeSplit.title);
          assert.deepEqual(source.content, beforeSplit.content);
          await model.redo(); await settle();
          assert.deepEqual(source.title, expected.before);
          await model.undo(); await settle();
        }

        // Typing while a middle split waits must not be overwritten by its
        // old snapshot; Undo removes only the redundant copy in this case.
        pressEnter(5); await settle();
        states.get('source').title = clone(source.title);
        states.get('source').title.content[0].content[0].text += ' during create';
        states.get('source').scheduleSave();
        const concurrentTitle = clone(states.get('source').title);
        releaseCreate(); await settle();
        assert.deepEqual(states.get('source').title, concurrentTitle);
        assert.equal((await saveCoordinator.flushAll()).ok, true);
        assert.deepEqual(source.title, concurrentTitle);
        const partialCopyId = `created-${sequence}`;
        await model.undo(); await settle();
        if (rows.some(row => row.id === partialCopyId)) {
          // People records the intervening typing as a later edit.
          const beforeCopyUndo = clone(source);
          await model.undo(); await settle();
          assert.deepEqual(source, beforeCopyUndo, 'Undo of the partial split only removes its copy');
        } else assert.deepEqual(source.title, concurrentTitle, 'Undo of the partial split preserves intervening typing');
        assert.equal(rows.some(row => row.id === partialCopyId), false);

        // A failed source write after creation retains the complete persisted
        // original and complete suffix copy, with an actionable save error.
        const beforeSaveFailure = clone(source);
        pressEnter(5, 5, true); await settle();
        failSave = true; releaseCreate(); await settle();
        assert.deepEqual(source, beforeSaveFailure);
        assert.deepEqual(rows.find(row => row.id === `created-${sequence}`).content, beforeSaveFailure.content);
        assert.equal(saveCoordinator.getSummary().failures.length, 1);
        assert.deepEqual(states.get('source').title, beforeSaveFailure.title, 'failed source save restores its full local title');
        assert.deepEqual(states.get('source').content, beforeSaveFailure.content);
        assert.equal(await model.undo(), false, 'a blocked save cannot undo by deleting the only suffix copy');
        const failedSplitCopy = `created-${sequence}`;
        assert.ok(rows.some(row => row.id === failedSplitCopy));
        failSave = false;
        assert.equal(await model.undo(), true); await settle();
        assert.equal(rows.some(row => row.id === failedSplitCopy), false);
        assert.deepEqual(source.title, beforeSaveFailure.title, 'retry then Undo retains complete original text');
        assert.deepEqual(source.content, beforeSaveFailure.content);

        // Two rapid structural commands, followed by Undo before the create
        // request completes, run in order rather than undoing an older edit.
        const beforeQueuedCreate = clone(source);
        const countBeforeQueuedCreate = rows.length;
        pressEnter(); await settle();
        const undoPendingCreate = model.undo();
        releaseCreate(); await undoPendingCreate; await settle();
        assert.equal(rows.length, countBeforeQueuedCreate);
        assert.deepEqual(source, beforeQueuedCreate);

        // Tiptap adds default paragraph attributes while serializing older
        // documents. This representation difference is not concurrent typing.
        const errorsBeforeDefaults = alerts.length;
        const expectedDefaults = splitTitleDocAtOffsets(source.title, 4, 4);
        pressEnter(5, 5, true); await settle(); releaseCreate(); await settle();
        assert.equal(alerts.length, errorsBeforeDefaults);
        assert.deepEqual(source.title.content[0].content, expectedDefaults.before.content[0].content);
        cases++;
      } finally { app.unmount(); await saveCoordinator.flushAll(); await nextTick(); }
    }
  }
} finally { for (const [key, value] of Object.entries(globals)) globalThis[key] = value; }
console.log(`Title Enter safety: ${cases} category/row cases, rich/selected splits, slow/failed writes, typing during creation and Undo/Redo`);
