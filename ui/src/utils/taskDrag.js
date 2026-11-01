// A distinct drag type lets whole-task moves bypass rich-text drop handling.
export const TASK_DRAG_TYPE = 'application/x-glance-task';
export const isTaskDrag = event => Array.from(event?.dataTransfer?.types || []).includes(TASK_DRAG_TYPE);
export const taskDropPosition = (rect, clientY) => clientY >= rect.top + rect.height / 2 ? 'after' : 'before';

// A weekday owns everything until the following heading's text begins,
// including margins/gaps and the leading whitespace of the next header.
export const weekdayDropTarget = (days, clientY) => {
  if (!days.length || !Number.isFinite(clientY)) return null;
  return days.findLast(day => clientY >= day.top) || days[0];
};
