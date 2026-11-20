import { taskGroup } from "../utils/taskGroups.js";

export function createTaskClipboardAdapter({ tasks, groupRows = tasks, record, refresh, defaultTask }) {
  return {
    record, refresh, defaultTask,
    destination(target, count) {
      target = taskGroup(groupRows(target), target).at(-1) || target;
      const next = tasks().filter(task => task.page === target.page && (task.ownerPersonId ?? null) === (target.ownerPersonId ?? null) && task.position > target.position)
        .sort((a, b) => a.position - b.position)[0];
      const base = target.position ?? Date.now();
      const step = next ? (next.position - base) / (count + 1) : 1;
      return index => ({
        page: target.page,
        ownerPersonId: target.ownerPersonId ?? null,
        position: base + step * (index + 1),
        scheduledDate: target.page === 'dashboard:main' ? target.scheduledDate ?? null : null,
        recurrence: null
      });
    }
  };
}
