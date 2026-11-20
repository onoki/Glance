import { ref, onMounted, onBeforeUnmount } from 'vue';
import { registerDesktopMessageHandler, sendDesktopMessage } from '../utils/desktopBridge.js';

export function useWindowRole() {
  const isHelperWindow = ref(false);
  const customTitleBar = ref(false);
  const maximized = ref(false);
  const captionScale = ref(1);
  let unsubscribe;
  let dragStart;
  const runWindowCommand = command => {
    if (customTitleBar.value) sendDesktopMessage({ type: 'windowCommand', command });
  };
  const beginTitleDrag = event => {
    if (event.button !== 0 || !event.isPrimary) return;
    dragStart = { id: event.pointerId, x: event.clientX, y: event.clientY };
    event.currentTarget.setPointerCapture(event.pointerId);
  };
  const endTitleDrag = event => {
    if (dragStart?.id === event.pointerId) dragStart = null;
  };
  const moveTitleDrag = event => {
    if (dragStart?.id !== event.pointerId || !(event.buttons & 1)) return;
    if (Math.hypot(event.clientX - dragStart.x, event.clientY - dragStart.y) < 4) return;
    endTitleDrag(event);
    event.currentTarget.releasePointerCapture(event.pointerId);
    runWindowCommand('move');
  };
  onMounted(() => {
    unsubscribe = registerDesktopMessageHandler(message => {
      if (message?.type === 'windowRole') isHelperWindow.value = message.role === 'helper';
      if (message?.type === 'windowFrame') {
        customTitleBar.value = true;
        maximized.value = !!message.maximized;
      }
      if ((message?.type === 'windowFrame' || message?.type === 'zoomState') && message.percent > 0) {
        captionScale.value = 100 / message.percent;
      }
    });
    sendDesktopMessage({ type: 'getWindowRole', customTitleBar: true });
  });
  onBeforeUnmount(() => unsubscribe?.());
  return { isHelperWindow, customTitleBar, maximized, captionScale, runWindowCommand, beginTitleDrag, moveTitleDrag, endTitleDrag };
}
