import { apiGet, apiPost } from "./client.js";

export const sendTaskToPeople = (taskId, personIds, tagIds, includeDashboard = false) => apiPost(`/api/tasks/${taskId}/send-to-people`, { personIds, tagIds, includeDashboard });
export const sendTaskCopies = async (selection, personIds, tagIds, includeDashboard = false) => {
  const tasks = Array.isArray(selection) ? selection : [selection];
  const response = tasks.length === 1
    ? await sendTaskToPeople(tasks[0].id, personIds, tagIds, includeDashboard)
    : await apiPost("/api/tasks/send-to-people", { taskIds: tasks.map(task => task.id), personIds, tagIds, includeDashboard });
  if (response.created > 0) for (const task of tasks) task.sendMarkerVisible = true;
  return response;
};
export const fetchTaskSendEvents = (taskId) => apiGet(`/api/tasks/${taskId}/send-events`);
export const dismissTaskSendMarker = (taskId) => apiPost(`/api/tasks/${taskId}/send-marker/dismiss`, {});
