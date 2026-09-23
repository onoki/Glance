export const pixelCorrection = (coordinate, scale) => Math.round(coordinate * scale) / scale - coordinate;

// Relative positioning preserves layout, selection geometry, and font size.
// Avoid transforms: compositing itself can change the font smoothing mode.
export function installPixelText(root) {
  let frame;
  const align = () => {
    frame = null;
    const scale = window.devicePixelRatio || 1;
    root.querySelectorAll('.ProseMirror, .pixel-text').forEach(element => {
      const rect = element.getBoundingClientRect();
      const x = parseFloat(element.style.getPropertyValue('--pixel-x')) || 0;
      const y = parseFloat(element.style.getPropertyValue('--pixel-y')) || 0;
      for (const [name, value, previous] of [
        ['--pixel-x', pixelCorrection(rect.x - x, scale), x],
        ['--pixel-y', pixelCorrection(rect.y - y, scale), y]
      ]) {
        if (Math.abs(value - previous) > 0.001) element.style.setProperty(name, `${value}px`);
      }
    });
  };
  const schedule = () => { if (!frame) frame = requestAnimationFrame(align); };
  const observer = new MutationObserver(schedule);
  observer.observe(root, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ['style', 'class'] });
  window.addEventListener('resize', schedule);
  document.addEventListener('scroll', schedule, true);
  root.addEventListener('transitionend', schedule);
  document.fonts?.ready.then(schedule);
  schedule();
  return () => {
    observer.disconnect(); cancelAnimationFrame(frame);
    window.removeEventListener('resize', schedule);
    document.removeEventListener('scroll', schedule, true);
    root.removeEventListener('transitionend', schedule);
  };
}
