import { planningWeekDays } from "../utils/categoryUtils.js";
import { ref } from "vue";
import { DASHBOARD_MAIN_PAGE, DASHBOARD_NEW_PAGE } from "../utils/pageConstants.js";
import { isTaskDrag, taskDropPositions } from "../utils/taskDrag.js";
import { detachBeforeMove, incompleteTaskGroup, taskGroupDrop } from "../utils/taskGroups.js";

export const useDashboardDrag = (options) => {
  const activeTab = options?.activeTab;
  const dashboardColumnsRef = options?.dashboardColumnsRef;
  const getCategoryTasks = options?.getCategoryTasks;
  const taskGroupRows = options?.taskGroupRows || (() => []);
  const findTaskById = options?.findTaskById;
  const applyTaskMove = options?.applyTaskMove;
  const applyTaskMoves = options?.applyTaskMoves;

  const dragState = ref(null);
  const dragOver = ref({ id: null, position: "before" });
  const dragOverDay = ref(null);
  let hoveredTaskId = null;
  const isDashboardDragging = ref(false);
  const dashboardDragState = ref({ active: false, startX: 0, startScrollLeft: 0, pointerId: null });

  const setDragOver = (taskId, position) => {
    dragOverDay.value = null;
    hoveredTaskId = taskId;
    if (!dragState.value || dragState.value.ids.includes(taskId)) {
      dragOver.value = { id: null, position: "before" };
      return;
    }
    const task = findTaskById?.(taskId);
    const rows = taskGroupRows(task);
    const drop = taskGroupDrop(rows, task, position || "before", draggedTasks(), taskGroupRows);
    dragOver.value = drop ? { id: drop.task?.id ?? taskId, position: drop.position } : { id: null, position: "before" };
  };

  const clearDragOver = (taskId) => {
    if (hoveredTaskId === taskId) {
      hoveredTaskId = null;
      dragOver.value = { id: null, position: "before" };
    }
  };

  const setWeekdayDragOver = (dateKey, event) => {
    if (!dragState.value || !isTaskDrag(event)) return;
    dragOver.value = { id: null, position: "before" };
    dragOverDay.value = !incompleteTaskGroup(draggedTasks(event), taskGroupRows)
      && planningWeekDays().some(day => day.dateKey === dateKey && !day.isPast) ? dateKey : null;
  };

  const clearWeekdayDragOver = (dateKey, event) => {
    if (event?.currentTarget?.contains?.(event.relatedTarget)) return;
    const rect = event?.currentTarget?.getBoundingClientRect?.();
    if (rect && event.clientX >= rect.left && event.clientX < rect.right && event.clientY >= rect.top && event.clientY < rect.bottom) return;
    if (dragOverDay.value === dateKey) dragOverDay.value = null;
  };

  const startDrag = (task, event, tasks = [task]) => {
    dragOverDay.value = null;
    dragState.value = { ids: tasks.map(row => row.id) };
    dragOver.value = { id: null, position: "before" };
    if (event?.dataTransfer) {
      event.dataTransfer.effectAllowed = "move";
      event.dataTransfer.setData("text/plain", task.id);
    }
  };

  const endDrag = () => {
    hoveredTaskId = null;
    dragOverDay.value = null;
    dragState.value = null;
    dragOver.value = { id: null, position: "before" };
  };

  const draggedTasks = (event) => {
    const ids = dragState.value?.ids || [event?.dataTransfer?.getData("text/plain")];
    const tasks = ids.map(id => id ? findTaskById?.(id) : null);
    return tasks.every(Boolean) ? tasks : [];
  };

  const applyDrop = (tasks, categoryId, previous, next, date) => {
    const positions = taskDropPositions(tasks.length, previous, next);
    return tasks.length === 1 ? applyTaskMove?.(tasks[0], categoryId, positions[0], date)
      : applyTaskMoves?.(tasks, categoryId, positions, date);
  };

  const dropOnTask = async (targetTask, categoryId, event) => {
    const tasks = draggedTasks(event);
    const ids = new Set(tasks.map(task => task.id));
    if (!tasks.length || ids.has(targetTask.id)) {
      return;
    }
    const day = planningWeekDays().find(day => day.dateKey === targetTask.scheduledDate);
    if (day?.isPast && tasks.some(task => task.scheduledDate !== targetTask.scheduledDate)) return;

    const dropPosition = hoveredTaskId === targetTask.id && dragOver.value.id ? dragOver.value.position : "before";
    const drop = taskGroupDrop(taskGroupRows(targetTask), targetTask, dropPosition, tasks, taskGroupRows);
    if (!drop) throw new Error(detachBeforeMove);
    const list = getCategoryTasks?.(targetTask.page, categoryId) || [];
    const filtered = list.filter((item) => !ids.has(item.id) && (!categoryId?.startsWith("week-") || item.scheduledDate === targetTask.scheduledDate));
    const targetIndex = filtered.findIndex((item) => item.id === drop.task.id);
    if (targetIndex < 0) {
      return;
    }
    const before = drop.position === "before";
    const prev = before ? (targetIndex > 0 ? filtered[targetIndex - 1] : null) : filtered[targetIndex];
    const next = before ? filtered[targetIndex] : (targetIndex + 1 < filtered.length ? filtered[targetIndex + 1] : null);
    const scheduledDateOverride = categoryId?.startsWith("week-") ? targetTask.scheduledDate : null;
    await applyDrop(tasks, categoryId, prev, next, scheduledDateOverride);
  };

  const dropOnCategory = async (categoryId, page, event, scheduledDateOverride = null) => {
    dragOverDay.value = null;
    const tasks = draggedTasks(event);
    if (!tasks.length) {
      return;
    }
    if (incompleteTaskGroup(tasks, taskGroupRows)) throw new Error(detachBeforeMove);
    const ids = new Set(tasks.map(task => task.id));
    const list = getCategoryTasks?.(page, categoryId).filter((item) => !ids.has(item.id) && (!scheduledDateOverride || item.scheduledDate === scheduledDateOverride));
    await applyDrop(tasks, categoryId, list.at(-1), null, scheduledDateOverride);
  };

  const dropOnCategoryFromView = (categoryId, event, scheduledDateOverride = null) => {
    const page = categoryId === "new" ? DASHBOARD_NEW_PAGE : DASHBOARD_MAIN_PAGE;
    return dropOnCategory(categoryId, page, event, scheduledDateOverride);
  };

  const dropOnWeekdayFromView = (categoryId, scheduledDate, event) => {
    dragOverDay.value = null;
    if (!planningWeekDays().some(day => day.dateKey === scheduledDate && !day.isPast)) return;
    return dropOnCategoryFromView(categoryId, event, scheduledDate);
  };

  const handleDashboardPointerDown = (event) => {
    const container = dashboardColumnsRef?.value;
    if (!container || activeTab?.value !== "Dashboard") {
      return;
    }
    if (event.button !== 0) {
      return;
    }
    const target = event.target;
    if (target?.closest?.(".ProseMirror") || target?.closest?.("input, textarea, select, button, label, summary, a, .task-meta, .floating-menu, .title-start-target")) {
      return;
    }
    if (target?.closest?.(".drag-handle")) {
      return;
    }
    if (target?.isContentEditable) {
      return;
    }
    dashboardDragState.value = {
      active: true,
      startX: event.clientX,
      startScrollLeft: container.scrollLeft,
      pointerId: event.pointerId
    };
    isDashboardDragging.value = true;
    container.setPointerCapture?.(event.pointerId);
    event.preventDefault();
  };

  const handleDashboardPointerMove = (event) => {
    const container = dashboardColumnsRef?.value;
    if (!container || !dashboardDragState.value.active) {
      return;
    }
    const delta = event.clientX - dashboardDragState.value.startX;
    container.scrollLeft = dashboardDragState.value.startScrollLeft - delta;
  };

  const handleDashboardPointerUp = () => {
    if (!dashboardDragState.value.active) {
      return;
    }
    const container = dashboardColumnsRef?.value;
    container?.releasePointerCapture?.(dashboardDragState.value.pointerId);
    dashboardDragState.value = { active: false, startX: 0, startScrollLeft: 0, pointerId: null };
    isDashboardDragging.value = false;
  };

  return {
    dragOver,
    dragOverDay,
    setWeekdayDragOver,
    clearWeekdayDragOver,
    isDashboardDragging,
    startDrag,
    endDrag,
    dropOnTask,
    dropOnCategory,
    dropOnCategoryFromView,
    dropOnWeekdayFromView,
    setDragOver,
    clearDragOver,
    handleDashboardPointerDown,
    handleDashboardPointerMove,
    handleDashboardPointerUp
  };
};
