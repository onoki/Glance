<template>
  <div class="zoom-controls" role="group" aria-label="Zoom">
    <button type="button" aria-label="Zoom out" title="Zoom out (Ctrl+−)" :disabled="percent <= MIN_ZOOM" @click="step(-1)">−</button>
    <button type="button" :title="tooltip" aria-label="Reset zoom to 100%" @click="setZoom(100)">{{ percent }}%{{ crisp ? '*' : '' }}</button>
    <button type="button" aria-label="Zoom in" title="Zoom in (Ctrl++)" :disabled="percent >= MAX_ZOOM" @click="step(1)">+</button>
  </div>
</template>
<script setup>
import { computed, onMounted, onBeforeUnmount, ref } from 'vue';
import { isDesktopBridgeAvailable, registerDesktopMessageHandler, sendDesktopMessage } from '../utils/desktopBridge.js';
import { MIN_ZOOM, MAX_ZOOM, isCrispZoom, zoomStep } from '../utils/zoom.js';
const percent = ref(100);
const scale = ref(window.devicePixelRatio || 1);
const desktop = isDesktopBridgeAvailable();
const crisp = computed(() => isCrispZoom(percent.value, scale.value));
const tooltip = computed(() => `${percent.value}% — click to reset to 100% (Ctrl+0). * means the font size is near a whole multiple of BigBlue’s native pixel grid on this monitor; it is likely to look sharper, but is not a guarantee. Intermediate zoom levels are allowed. Windows display scaling is preserved.`);
function setZoom(value) {
  percent.value = Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, value));
  if (desktop) sendDesktopMessage({ type: 'setZoom', percent: percent.value });
  else {
    document.documentElement.style.zoom = `${percent.value}%`;
    try { localStorage.setItem('glance-browser-zoom', String(percent.value)); } catch { /* storage may be disabled */ }
    window.dispatchEvent(new Event('resize'));
  }
}
const step = direction => setZoom(zoomStep(percent.value, direction, scale.value));
let wheelTime = -Infinity;
const onWheel = event => {
  if (!event.ctrlKey || !event.deltaY) return;
  event.preventDefault();
  if (performance.now() - wheelTime < 100) return;
  wheelTime = performance.now();
  step(event.deltaY < 0 ? 1 : -1);
};
const onKey = event => {
  if (!(event.ctrlKey || event.metaKey) || event.altKey || !['+', '=', '-', '0'].includes(event.key)) return;
  event.preventDefault();
  event.stopPropagation();
  if (event.key === '0') setZoom(100); else step(event.key === '-' ? -1 : 1);
};
let unsubscribe;
const onResize = () => { if (!desktop) scale.value = window.devicePixelRatio || 1; };
onMounted(() => {
  unsubscribe = registerDesktopMessageHandler(message => {
    if (message?.type !== 'zoomState') return;
    percent.value = message.percent;
    scale.value = message.scale;
  });
  if (desktop) sendDesktopMessage({ type: 'zoomSync' });
  else {
    try { const saved = Number(localStorage.getItem('glance-browser-zoom')); if (saved >= MIN_ZOOM && saved <= MAX_ZOOM) setZoom(saved); } catch { /* storage may be disabled */ }
  }
  window.addEventListener('wheel', onWheel, { passive: false, capture: true });
  window.addEventListener('keydown', onKey, true);
  window.addEventListener('resize', onResize);
});
onBeforeUnmount(() => {
  unsubscribe?.();
  window.removeEventListener('wheel', onWheel, true);
  window.removeEventListener('keydown', onKey, true);
  window.removeEventListener('resize', onResize);
});
</script>
<style scoped>
.zoom-controls { display: flex; align-items: center; gap: 1px; margin-right: 6px; }
</style>
