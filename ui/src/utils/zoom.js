export const MIN_ZOOM = 50;
export const MAX_ZOOM = 400;
// Font height is 8 CSS pixels on a native 12-pixel design grid.
export function isCrispZoom(percent, scale) {
  const pixels = 8 * scale * percent / 100;
  const multiple = Math.round(pixels / 12);
  return multiple >= 1 && Math.abs(pixels - multiple * 12) <= .061;
}
export function zoomStep(percent, direction, scale) {
  const levels = new Set(Array.from({ length: 36 }, (_, i) => 50 + i * 10));
  for (let multiple = 1; multiple <= 12; multiple++) {
    const level = Math.round(150 * multiple / scale);
    if (level >= MIN_ZOOM && level <= MAX_ZOOM) levels.add(level);
  }
  const sorted = [...levels].sort((a, b) => a - b);
  return (direction > 0 ? sorted.find(level => level > percent) : sorted.reverse().find(level => level < percent)) ?? percent;
}
