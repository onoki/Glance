const API_BASE = import.meta.env.VITE_API_BASE || "";

const request = async (method, path, body) => {
  const response = await fetch(`${API_BASE}${path}`, {
    method,
    headers: {
      "Content-Type": "application/json"
    },
    body: body ? JSON.stringify(body) : undefined
  });

  if (!response.ok) {
    let message = response.statusText;
    try {
      const payload = await response.json();
      message = payload.message || message;
    } catch {
      // ignore parsing errors
    }
    throw new Error(message);
  }

  return response.json();
};

export const apiGet = (path) => request("GET", path);
export const apiPost = (path, body) => request("POST", path, body);
export const apiPut = (path, body) => request("PUT", path, body);
