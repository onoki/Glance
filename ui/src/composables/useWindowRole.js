import { ref, onMounted, onBeforeUnmount } from 'vue';
import { registerDesktopMessageHandler, sendDesktopMessage } from '../utils/desktopBridge.js';

export function useWindowRole() {
  const isHelperWindow = ref(false);
  let unsubscribe;
  onMounted(() => {
    unsubscribe = registerDesktopMessageHandler(message => {
      if (message?.type === 'windowRole') isHelperWindow.value = message.role === 'helper';
    });
    sendDesktopMessage({ type: 'getWindowRole' });
  });
  onBeforeUnmount(() => unsubscribe?.());
  return { isHelperWindow };
}
