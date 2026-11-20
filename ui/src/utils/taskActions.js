import { shouldDeleteEmptyOnComplete } from "./taskCompletionUtils.js";
import { taskRevision } from "./taskHistory.js";

export function taskActionChanges(tasks, action, value) {
  const isPrivate = !tasks.every(task => task.isPrivate);
  const completedAt = tasks.every(task => task.completedAt) ? null : Date.now();
  return tasks.map(task => {
    switch (action) {
      case "privacy":
        return { task, fields: { isPrivate } };
      case "complete":
        return completedAt && shouldDeleteEmptyOnComplete(task)
          ? { task, deleted: true }
          : { task, fields: { completedAt: completedAt ? task.completedAt ?? completedAt : null } };
      case "delete":
        return { task, deleted: true };
      case "recurrence":
        return { task, fields: { recurrence: value } };
      case "dismiss-send":
        return { task, fields: { dismissSendMarker: true } };
      default:
        throw new Error("This task action is unavailable.");
    }
  });
}

export const taskActionUpdates = changes => changes.map(({ task, fields, deleted }) => ({
  taskId: task.id,
  update: { baseUpdatedAt: taskRevision(task), ...fields },
  ...(deleted ? { delete: true } : {})
}));
