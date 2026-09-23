import { shallowRef } from 'vue';
export const actionFeedback = shallowRef(null);
let timer;
export function announceAction(text, undo = null, canUndo = () => true) {
  clearTimeout(timer);
  actionFeedback.value = { text, undo, canUndo };
  timer = setTimeout(() => { actionFeedback.value = null; }, 6000);
}
export function dismissAction() { clearTimeout(timer); actionFeedback.value = null; }
