import { apiGet, apiPost, apiPut } from "./client.js";

export const fetchBackupCatalog = () => apiGet("/api/backups");
export const createVerifiedBackup = () => apiPost("/api/backups", {});
export const verifyBackup = (backupId) => apiPost(`/api/backups/${encodeURIComponent(backupId)}/verify`, {});

export const fetchDataSafetySettings = () => apiGet("/api/data-safety/settings");
export const saveDataSafetySettings = (settings) => apiPut("/api/data-safety/settings", settings);
export const testBackupLocation = (location) => apiPost("/api/data-safety/test-location", { location });
export const stageBackupRestore = (backupId) => apiPost("/api/data-safety/restore", { backupId });
export const fetchStartupSafety = () => apiGet("/api/data-safety/startup");
