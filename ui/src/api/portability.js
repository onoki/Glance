import { saveDownload } from "./client.js";

export const exportAllNotes = () => saveDownload("/api/export/portable");
