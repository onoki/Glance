import { apiGet } from "./client.js";

export const fetchHistory = () => apiGet("/api/history");
