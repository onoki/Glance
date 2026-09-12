import { apiGet, apiPost, apiUpload, saveDownload } from "./client.js";

export const fetchStatusOverview = () => apiGet("/api/status-updates");
export const authenticateStatusSources = () => apiPost("/api/status-updates/authenticate", {});
export const collectStatusInput = (payload) => apiPost("/api/status-updates/collect", payload);
export const importStatusSummary = (file) => {
  const form = new FormData();
  form.append("summary", file, file.name);
  return apiUpload("/api/status-updates/import", form);
};
export const downloadStatusJson = () => saveDownload("/api/status-updates/json");
export const downloadStatusSchema = () => saveDownload("/api/status-updates/schema");
export const downloadStatusExcel = () => saveDownload("/api/status-updates/excel");
export const downloadStatusPowerPoint = () => saveDownload("/api/status-updates/powerpoint");
