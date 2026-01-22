import { apiDelete, apiGet, apiPost, apiPut } from "./client.js";

export const fetchDashboard = () => apiGet("/api/dashboard");

export const createTask = (payload) => apiPost("/api/tasks", payload);

export const updateTask = (id, payload) => apiPut(`/api/tasks/${id}`, payload);

export const deleteTask = (id) => apiDelete(`/api/tasks/${id}`);

export const completeTask = (id, payload) => apiPost(`/api/tasks/${id}/complete`, payload);

export const fetchChanges = (since) => apiGet(`/api/changes?since=${since}`);

export const runRecurrence = () => apiPost("/api/recurrence/run", {});

export const moveCompletedToHistory = () => apiPost("/api/history/move-completed-to-history", {});
