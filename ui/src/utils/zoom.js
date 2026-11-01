export const MIN_ZOOM = 50;
export const MAX_ZOOM = 400;
// Keep in sync with compact.css. Outline fonts have no native bitmap grid.
export const BASE_FONT_SIZE = 7;
export function isPixelAlignedZoom(percent, scale) {
  const pixels = BASE_FONT_SIZE * scale * percent / 100;
  return Number.isFinite(pixels) && pixels >= 1 && Math.abs(pixels - Math.round(pixels)) <= .04;
}
export function zoomStep(percent, direction, scale) {
  const levels = new Set(Array.from({ length: 36 }, (_, i) => 50 + i * 10));
  if (Number.isFinite(scale) && scale > 0) {
    const maxPixels = Math.floor(BASE_FONT_SIZE * scale * MAX_ZOOM / 100);
    for (let pixels = 1; pixels <= maxPixels; pixels++) {
      const level = Math.round(100 * pixels / (BASE_FONT_SIZE * scale));
      if (level >= MIN_ZOOM && level <= MAX_ZOOM && isPixelAlignedZoom(level, scale)) levels.add(level);
    }
  }
  const sorted = [...levels].sort((a, b) => a - b);
  return (direction > 0 ? sorted.find(level => level > percent) : sorted.reverse().find(level => level < percent)) ?? percent;
}
