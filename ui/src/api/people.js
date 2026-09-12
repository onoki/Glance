import { apiDelete, apiGet, apiPost, apiPut } from "./client.js";

export const fetchPeople = (includeArchived = false) => apiGet(`/api/people?includeArchived=${includeArchived}`);
export const createPerson = (displayName) => apiPost("/api/people", { displayName });
export const updatePerson = (id, payload) => apiPut(`/api/people/${id}`, payload);
export const setPersonTags = (id, tagIds) => apiPut(`/api/people/${id}/tags`, { tagIds });
export const fetchPersonTasks = (id) => apiGet(`/api/people/${id}/tasks`);
export const createPersonTag = (name) => apiPost("/api/people/tags", { name });
export const updatePersonTag = (id, payload) => apiPut(`/api/people/tags/${id}`, payload);
export const deletePersonTag = (id) => apiDelete(`/api/people/tags/${id}`);
