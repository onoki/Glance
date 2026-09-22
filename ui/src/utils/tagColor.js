// Stable fallback for tags without a user-selected color.
export const tagColor = (id) => {
  let hash = 0;
  for (const char of String(id)) hash = ((hash * 31) + char.charCodeAt(0)) >>> 0;
  return ["#a52a2a", "#2473b5", "#378047", "#8454ad", "#b36713", "#167f80", "#b63c77", "#616b22"][hash % 8];
};

// Native color pickers emit many input events before closing. Serialize writes
// per tag and skip superseded colors so a slow request cannot win over the last.
export function createTagColorSaver(save) {
  const pending = new Map();
  return (id, color) => {
    const existing = pending.get(id);
    if (existing) {
      existing.color = color;
      return existing.promise;
    }
    const entry = { color };
    pending.set(id, entry);
    entry.promise = (async () => {
      try {
        let saved;
        do {
          saved = entry.color;
          await save(id, saved);
        } while (saved !== entry.color);
        return saved;
      } finally {
        pending.delete(id);
      }
    })();
    return entry.promise;
  };
}
