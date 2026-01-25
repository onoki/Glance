const API_BASE = import.meta.env.VITE_API_BASE || "";

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
    const origin = API_BASE || window.location.origin;
    throw new Error(`Network error calling ${origin}${path}: ${error?.message || "Failed to fetch"}`);
  }

  if (!response.ok) {
    let message = response.statusText;
    try {
      const payload = await response.json();
      message = payload.message || message;
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
    throw new Error(message);
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
    const origin = API_BASE || window.location.origin;
    throw new Error(`Network error calling ${origin}${path}: ${error?.message || "Failed to fetch"}`);
  }

  if (!response.ok) {
    let message = response.statusText;
    try {
      const payload = await response.json();
      message = payload.message || message;
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
    throw new Error(message);
  }

  return response.json();
};
