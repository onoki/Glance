// A distinct drag type lets whole-task moves bypass rich-text drop handling.
export const TASK_DRAG_TYPE = 'application/x-glance-task';
export const isTaskDrag = event => Array.from(event?.dataTransfer?.types || []).includes(TASK_DRAG_TYPE);
export const taskDropPosition = (rect, clientY) => clientY >= rect.top + rect.height / 2 ? 'after' : 'before';
