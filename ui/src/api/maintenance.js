import { apiGet, apiPost } from "./client.js";

export const fetchWarnings = () => apiGet("/api/warnings");

export const fetchVersion = () => apiGet("/api/version");

export const fetchMaintenanceStatus = () => apiGet("/api/maintenance/status");

export const triggerBackup = () => apiPost("/api/backup", {});

export const triggerReindex = () => apiPost("/api/search/reindex", {});

export const runDailyMaintenance = () => apiPost("/api/maintenance/daily", {});
