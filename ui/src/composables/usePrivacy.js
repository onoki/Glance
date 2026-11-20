import { ref } from 'vue';

// Each window starts protected. The task flag is durable; revealing is only UI
// state and never changes the documents or another window's presentation.
export const privacyMode = ref(true);
export const privacyPresentationKey = Symbol('privacy presentation');
export const revealedPrivateTask = ref(null);
export const revealPrivateTask = id => { revealedPrivateTask.value = id; };

export function installPrivacy({ host = window, doc = document } = {}) {
  let focusMayReveal = true;
  const row = target => target?.closest?.('[data-task-id]')?.dataset.taskId || null;
  const blur = () => { focusMayReveal = false; revealedPrivateTask.value = null; };
  const down = () => { focusMayReveal = true; };
  const focus = event => { revealedPrivateTask.value = focusMayReveal ? row(event.target) : null; };
  const out = event => { if (row(event.relatedTarget) !== revealedPrivateTask.value) revealedPrivateTask.value = null; };
  const interact = event => { focusMayReveal = true; revealedPrivateTask.value = row(event.target); };
  host.addEventListener('blur', blur);
  doc.addEventListener('mousedown', down, true);
  doc.addEventListener('focusin', focus, true);
  doc.addEventListener('focusout', out, true);
  doc.addEventListener('keydown', interact, true);
  doc.addEventListener('click', interact);
  return () => {
    host.removeEventListener('blur', blur);
    doc.removeEventListener('mousedown', down, true);
    doc.removeEventListener('focusin', focus, true);
    doc.removeEventListener('focusout', out, true);
    doc.removeEventListener('keydown', interact, true);
    doc.removeEventListener('click', interact);
    revealedPrivateTask.value = null;
  };
}
