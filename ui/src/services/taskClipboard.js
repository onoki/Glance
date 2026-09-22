import { shallowRef } from 'vue';
import { apiGet, absoluteApiUrl } from '../api/client.js';
import { createTask, deleteTask } from '../api/tasks.js';
import { flushAllSaves, saveCoordinator } from './saveCoordinator.js';
import { clipboardHasImages, decodeTaskClipboard, encodeTaskClipboard } from '../utils/taskClipboardFormat.js';
import { createClipboardHistory, sameClipboardTask } from './taskClipboardHistory.js';

const clone = value => JSON.parse(JSON.stringify(value));
export const taskClipboard = {
  selected: shallowRef(new Set()), busy: shallowRef(false), rows: new Map(), anchor: null, scope: null,
  register(id, row) {
    this.rows.set(id, row);
    return () => {
      if (this.rows.get(id) !== row) return;
      this.rows.delete(id);
      const next = new Set(this.selected.value); next.delete(id); this.selected.value = next;
    };
  },
  ordered() {
    return [...this.rows.entries()].filter(([, row]) => row.element()?.isConnected)
      .sort((a, b) => a[1].element().compareDocumentPosition(b[1].element()) & 4 ? -1 : 1);
  },
  clear() { this.selected.value = new Set(); },
  select(id, event = {}) {
    const ordered = this.ordered();
    const row = this.rows.get(id);
    if (!row || this.busy.value) return;
    const next = event.ctrlKey || event.metaKey ? new Set(this.selected.value) : new Set();
    if (event.shiftKey && this.anchor && this.rows.get(this.anchor)?.adapter === row.adapter) {
      const ids = ordered.filter(([, item]) => item.adapter === row.adapter).map(([key]) => key);
      const a = ids.indexOf(this.anchor), b = ids.indexOf(id);
      for (const key of ids.slice(Math.min(a, b), Math.max(a, b) + 1)) next.add(key);
    } else {
      if (next.has(id)) next.delete(id); else next.add(id);
      this.anchor = id;
    }
    this.selected.value = next;
    window.getSelection()?.removeAllRanges();
    row.handle()?.focus();
  },
  async copy(cut = false, event = null) {
    if (this.busy.value) return;
    const rows = this.ordered().filter(([id]) => this.selected.value.has(id)).map(([, row]) => row);
    if (!rows.length) return;
    if (rows.some(row => row.adapter !== rows[0].adapter)) throw new Error('Select tasks within one view at a time.');
    const snapshots = rows.map(row => clone(row.snapshot()));
    const payload = encodeTaskClipboard(snapshots, this.scope);
    if (event?.clipboardData) {
      event.clipboardData.setData('text/plain', payload.text);
      event.clipboardData.setData('text/html', payload.html);
    }
    this.busy.value = true;
    const records = [];
    try {
      if (!navigator.clipboard?.write || typeof ClipboardItem === 'undefined') throw new Error('Whole-task copy requires clipboard access. Nothing was cut.');
      await navigator.clipboard.write([new ClipboardItem({
        'text/plain': new Blob([payload.text], { type: 'text/plain' }),
        'text/html': new Blob([payload.html], { type: 'text/html' })
      })]);
      if (!cut) return;
      if (!(await flushAllSaves()).ok) throw new Error('Clipboard copied, but cutting stopped because some edits could not be saved.');
      for (const [index, row] of rows.entries()) {
        if (!row.element()?.isConnected) throw new Error('Cutting stopped because the source view changed. The clipboard still contains your tasks.');
        const task = clone(row.snapshot());
        if (!sameClipboardTask(task, snapshots[index])) throw new Error('A task changed while copying. Cutting stopped to preserve the newer text.');
        const record = { task, present: null };
        records.push(record); // retain undo intent even if a deletion response is lost
        await deleteTask(task.id, task.updatedAt);
        saveCoordinator.forget(task.id);
        record.present = false;
      }
    } finally {
      try {
        if (records.length) {
          const adapter = rows[0].adapter;
          adapter.record(createClipboardHistory(records, 'cut', adapter.refresh));
          this.clear();
          await adapter.refresh();
        }
      } finally { this.busy.value = false; }
    }
  },
  async paste(data, target) {
    if (this.busy.value || !target) return;
    if (clipboardHasImages(data) && (!this.scope || data.scope !== this.scope)) {
      throw new Error('These attachments belong to another Glance database. Paste them in the original database; cross-database attachment transfer is not supported.');
    }
    this.busy.value = true;
    const records = [];
    const adapter = target.adapter;
    // Capture the destination before any asynchronous operation or navigation.
    try {
      const destination = adapter.destination(target.snapshot(), data.tasks.length);
      if (!(await flushAllSaves()).ok) throw new Error('Paste stopped because some edits could not be saved.');
      for (let i = 0; i < data.tasks.length; i++) {
        const task = clone(data.tasks[i]);
        const rebase = node => {
          if (node.type === 'image') node.attrs.src = absoluteApiUrl(node.attrs.src);
          for (const child of node.content || []) rebase(child);
        };
        rebase(task.title); rebase(task.content);
        const payload = { ...destination(i), title: task.title, content: task.content, recurrence: null };
        const response = await createTask(payload);
        records.push({ task: { ...payload, id: response.taskId, completedAt: null }, present: true });
      }
    } finally {
      try {
        if (records.length) {
          adapter.record(createClipboardHistory(records, 'paste', adapter.refresh));
          this.clear();
          await adapter.refresh();
        }
      } finally { this.busy.value = false; }
    }
  },
  install(getDefaultTarget) {
    void apiGet('/api/clipboard-scope').then(data => { this.scope = data.scope; }).catch(() => {});
    const report = promise => { void promise.catch(error => window.alert(error.message || 'Task clipboard operation failed.')); };
    const rowAt = target => this.rows.get(target?.closest?.('[data-task-id]')?.dataset.taskId);
    const onKey = event => {
      const row = rowAt(event.target);
      if ((event.ctrlKey || event.metaKey) && event.shiftKey && event.code === 'Space' && row) {
        event.preventDefault(); event.stopImmediatePropagation(); this.select(row.snapshot().id); return;
      }
      if (event.key === 'Escape' && this.selected.value.size) {
        event.preventDefault(); const focused = row || this.rows.get(this.anchor); this.clear(); focused?.focusEditor(); return;
      }
      if (row && event.target?.closest?.('.task-select-handle') && ['ArrowUp', 'ArrowDown'].includes(event.key)) {
        const rows = this.ordered().filter(([, item]) => item.adapter === row.adapter);
        const index = rows.findIndex(([, item]) => item === row);
        const next = rows[index + (event.key === 'ArrowDown' ? 1 : -1)];
        if (next) { event.preventDefault(); this.select(next[0], { shiftKey: event.shiftKey }); }
      }
    };
    const onCopy = event => {
      if (!this.selected.value.size) return;
      event.preventDefault(); event.stopImmediatePropagation(); report(this.copy(event.type === 'cut', event));
    };
    const onPaste = event => {
      if (event.target?.closest?.('input, textarea, select')) return;
      const html = event.clipboardData?.getData('text/html');
      let data;
      try { data = decodeTaskClipboard(html); }
      catch (error) { event.preventDefault(); event.stopImmediatePropagation(); window.alert(error.message); return; }
      if (!data) return; // ordinary text paste stays with the editor
      const row = rowAt(event.target) || this.rows.get(this.anchor) || getDefaultTarget();
      if (!row) return;
      event.preventDefault(); event.stopImmediatePropagation(); report(this.paste(data, row));
    };
    const onFocus = event => { if (!event.target?.closest?.('.task-select-handle')) { this.clear(); this.anchor = null; } };
    document.addEventListener('keydown', onKey, true);
    document.addEventListener('copy', onCopy, true);
    document.addEventListener('cut', onCopy, true);
    document.addEventListener('paste', onPaste, true);
    document.addEventListener('focusin', onFocus, true);
    return () => {
      document.removeEventListener('keydown', onKey, true);
      document.removeEventListener('copy', onCopy, true);
      document.removeEventListener('cut', onCopy, true);
      document.removeEventListener('paste', onPaste, true);
      document.removeEventListener('focusin', onFocus, true);
      this.clear(); this.rows.clear();
    };
  }
};
