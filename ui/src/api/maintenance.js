import { apiGet, apiPost, apiUpload } from "./client.js";

export const fetchWarnings = () => apiGet("/api/warnings");

export const fetchVersion = () => apiGet("/api/version");

export const fetchMaintenanceStatus = () => apiGet("/api/maintenance/status");

export const triggerBackup = () => apiPost("/api/backup", {});

export const triggerReindex = () => apiPost("/api/search/reindex", {});

export const runDailyMaintenance = () => apiPost("/api/maintenance/daily", {});

export const resetRecurrence = () => apiPost("/api/recurrence/reset", {});

export const installUpdate = (file) => {
  const formData = new FormData();
  formData.append("package", file);
  return apiUpload("/api/update", formData);
};
