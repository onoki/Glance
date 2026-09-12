const API_BASE = import.meta.env?.VITE_API_BASE || "";

const networkError = (method, path, error) => {
  const origin = API_BASE || window.location.origin;
  const wrapped = new Error(`Network error calling ${origin}${path}: ${error?.message || "Failed to fetch"}`);
  wrapped.name = "NetworkError";
  wrapped.isNetworkError = true;
  wrapped.method = method;
  wrapped.path = path;
  return wrapped;
};

const httpError = (response, message, payload = null) => {
  const error = new Error(message || response.statusText || `HTTP ${response.status}`);
  error.name = response.status === 409 ? "ConflictError" : "HttpError";
  error.status = response.status;
  error.payload = payload;
  if (Number.isFinite(payload?.currentUpdatedAt)) {
    error.currentUpdatedAt = payload.currentUpdatedAt;
  }
  return error;
};

const request = async (method, path, body) => {
  let response;
  try {
    response = await fetch(`${API_BASE}${path}`, {
      method,
      headers: {
        "Content-Type": "application/json"
      },
      body: body ? JSON.stringify(body) : undefined,
      cache: "no-store"
    });
  } catch (error) {
    throw networkError(method, path, error);
  }

  if (!response.ok) {
    let message = response.statusText;
    let payload = null;
    try {
      payload = await response.json();
      message = payload.message || message;
      if (Array.isArray(payload.errors) && payload.errors.length) {
        message = `${message}\n${payload.errors.join("\n")}`;
      }
    } catch {
      try {
        const text = await response.text();
        if (text) {
          message = text;
        }
      } catch {
        // ignore parsing errors
      }
    }
    throw httpError(response, message, payload);
  }

  return response.json();
};

export const apiGet = (path) => request("GET", path);
export const apiPost = (path, body) => request("POST", path, body);
export const apiPut = (path, body) => request("PUT", path, body);
export const apiDelete = (path) => request("DELETE", path);

export const apiUpload = async (path, formData) => {
  let response;
  try {
    response = await fetch(`${API_BASE}${path}`, {
      method: "POST",
      body: formData,
      cache: "no-store"
    });
  } catch (error) {
    throw networkError("POST", path, error);
  }

  if (!response.ok) {
    let message = response.statusText;
    let payload = null;
    try {
      payload = await response.json();
      message = payload.message || message;
      if (Array.isArray(payload.errors) && payload.errors.length) {
        message = `${message}\n${payload.errors.join("\n")}`;
      }
    } catch {
      try {
        const text = await response.text();
        if (text) {
          message = text;
        }
      } catch {
        // ignore parsing errors
      }
    }
    throw httpError(response, message, payload);
  }

  return response.json();
};

export const apiDownload = async (path) => {
  const response = await fetch(`${API_BASE}${path}`, { cache: "no-store" });
  if (!response.ok) {
    let message = response.statusText;
    try {
      const payload = await response.json();
      message = payload.message || message;
    } catch { /* response was not JSON */ }
    throw new Error(message);
  }
  const disposition = response.headers.get("content-disposition") || "";
  const match = disposition.match(/filename\*?=(?:UTF-8''|")?([^";]+)/i);
  return { blob: await response.blob(), filename: decodeURIComponent(match?.[1] || "download") };
};

export const saveDownload = async (path) => {
  const { blob, filename } = await apiDownload(path);
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = filename;
  anchor.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
};
