// Stable across navigation, reordering, and windows; no persisted color needed.
export const tagColor = (id) => {
  let hash = 0;
  for (const char of String(id)) hash = ((hash * 31) + char.charCodeAt(0)) >>> 0;
  return `hsl(${hash % 360} 58% 42%)`;
};
