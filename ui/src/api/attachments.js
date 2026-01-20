import { apiUpload } from "./client.js";

export const uploadAttachment = (formData) => apiUpload("/api/attachments", formData);
