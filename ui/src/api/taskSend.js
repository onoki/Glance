import { apiGet, apiPost } from "./client.js";

export const sendTaskToPeople = (taskId, personIds, tagIds, includeDashboard = false) => apiPost(`/api/tasks/${taskId}/send-to-people`, { personIds, tagIds, includeDashboard });
export const fetchTaskSendEvents = (taskId) => apiGet(`/api/tasks/${taskId}/send-events`);
export const dismissTaskSendMarker = (taskId) => apiPost(`/api/tasks/${taskId}/send-marker/dismiss`, {});
