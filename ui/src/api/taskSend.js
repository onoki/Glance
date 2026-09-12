import { apiGet, apiPost, apiPut } from "./client.js";

export const setStatusMarkers = (taskId, payload) => apiPut(`/api/tasks/${taskId}/status-markers`, payload);
export const sendTaskToPeople = (taskId, personIds, tagIds) => apiPost(`/api/tasks/${taskId}/send-to-people`, { personIds, tagIds });
export const sendTaskToDashboard = (taskId) => apiPost(`/api/tasks/${taskId}/send-to-dashboard`, {});
export const fetchTaskSendEvents = (taskId) => apiGet(`/api/tasks/${taskId}/send-events`);
export const dismissTaskSendMarker = (taskId) => apiPost(`/api/tasks/${taskId}/send-marker/dismiss`, {});
