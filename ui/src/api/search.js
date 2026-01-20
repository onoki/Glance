import { apiGet, apiPost } from "./client.js";

export const searchTasks = (query) =>
  apiGet(`/api/search?q=${encodeURIComponent(query)}`);

export const reindexSearch = () => apiPost("/api/search/reindex", {});
