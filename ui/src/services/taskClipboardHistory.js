import * as taskApi from '../api/tasks.js';
import { apiGet } from '../api/client.js';
import { saveCoordinator } from './saveCoordinator.js';

const canonical = value => JSON.stringify(value, (_, item) => item && typeof item === 'object' && !Array.isArray(item)
  ? Object.fromEntries(Object.keys(item).sort().map(key => [key, item[key]])) : item);
const contentOf = task => [task.title, task.content, task.page, task.ownerPersonId ?? null,
  task.scheduledDate ?? null, task.recurrence ?? null, task.completedAt ?? null];
export const sameClipboardTask = (left, right) => canonical(contentOf(left)) === canonical(contentOf(right));

export function createClipboardHistory(records, kind, refresh, api = taskApi, fetchTasks = async task => {
  if (task.ownerPersonId) return (await apiGet(`/api/people/${task.ownerPersonId}/tasks`)).tasks;
  const result = await apiGet('/api/dashboard');
  return [...result.newTasks, ...result.mainTasks];
}) {
  const apply = async (present) => {
    try {
      for (const record of records) {
        const current = (await fetchTasks(record.task)).find(task => task.id === record.task.id);
        if (present) {
          if (!current) await api.restoreTask(record.task.id);
        } else {
          if (!current) { record.present = false; continue; }
          if (!sameClipboardTask(current, record.task)) {
            throw new Error('A clipboard task changed elsewhere. Undo/Redo stopped to preserve those changes.');
          }
          await api.deleteTask(record.task.id, current.updatedAt);
          saveCoordinator.forget(record.task.id);
        }
        // Keep progress on partial failures so retry does not replay completed work.
        record.present = present;
      }
    } finally { await refresh(); }
    return true;
  };
  return { undo: () => apply(kind === 'cut'), redo: () => apply(kind !== 'cut') };
}
