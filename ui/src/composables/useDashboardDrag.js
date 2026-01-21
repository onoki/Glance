import { ref } from "vue";
import { DASHBOARD_MAIN_PAGE, DASHBOARD_NEW_PAGE } from "../utils/pageConstants.js";

export const useDashboardDrag = (options) => {
  const activeTab = options?.activeTab;
  const dashboardColumnsRef = options?.dashboardColumnsRef;
  const getCategoryTasks = options?.getCategoryTasks;
  const findTaskById = options?.findTaskById;
  const applyTaskMove = options?.applyTaskMove;

  const dragState = ref(null);
  const dragOver = ref({ id: null, position: "before" });
  const isDashboardDragging = ref(false);
  const dashboardDragState = ref({ active: false, startX: 0, startScrollLeft: 0, pointerId: null });

  const setDragOver = (taskId, position) => {
    dragOver.value = { id: taskId, position: position || "before" };
  };

  const clearDragOver = (taskId) => {
    if (dragOver.value.id === taskId) {
      dragOver.value = { id: null, position: "before" };
    }
  };

  const startDrag = (task, event) => {
    dragState.value = { taskId: task.id, page: task.page };
    dragOver.value = { id: task.id, position: "before" };
    if (event?.dataTransfer) {
      event.dataTransfer.effectAllowed = "move";
      event.dataTransfer.setData("text/plain", task.id);
    }
  };

  const endDrag = () => {
    dragState.value = null;
    dragOver.value = { id: null, position: "before" };
  };

  const dropOnTask = async (targetTask, categoryId, event) => {
    const dragId = dragState.value?.taskId || event?.dataTransfer?.getData("text/plain");
    const dragged = dragId ? findTaskById?.(dragId) : null;
    if (!dragged || dragged.id === targetTask.id) {
      return;
    }
    if (dragged.page !== targetTask.page) {
      return;
    }

    const list = getCategoryTasks?.(targetTask.page, categoryId);
    const filtered = list.filter((item) => item.id !== dragged.id);
    const targetIndex = filtered.findIndex((item) => item.id === targetTask.id);
    if (targetIndex < 0) {
      return;
    }
    const dropPosition = dragOver.value.id === targetTask.id ? dragOver.value.position : "before";
    const before = dropPosition === "before";
    const prev = before ? (targetIndex > 0 ? filtered[targetIndex - 1] : null) : filtered[targetIndex];
    const next = before ? filtered[targetIndex] : (targetIndex + 1 < filtered.length ? filtered[targetIndex + 1] : null);
    const position = next && prev ? (prev.position + next.position) / 2 : prev ? prev.position + 1 : next.position - 1;
    const scheduledDateOverride = categoryId?.startsWith("week-") ? targetTask.scheduledDate : null;
    await applyTaskMove?.(dragged, categoryId, position, scheduledDateOverride);
  };

  const dropOnCategory = async (categoryId, page, event, scheduledDateOverride = null) => {
    const dragId = dragState.value?.taskId || event?.dataTransfer?.getData("text/plain");
    const dragged = dragId ? findTaskById?.(dragId) : null;
    if (!dragged || dragged.page !== page) {
      return;
    }
    const list = getCategoryTasks?.(page, categoryId).filter((item) => item.id !== dragged.id);
    const last = list[list.length - 1] || null;
    const position = last ? last.position + 1 : Date.now();
    await applyTaskMove?.(dragged, categoryId, position, scheduledDateOverride);
  };

  const dropOnCategoryFromView = (categoryId, event, scheduledDateOverride = null) => {
    const page = categoryId === "new" ? DASHBOARD_NEW_PAGE : DASHBOARD_MAIN_PAGE;
    return dropOnCategory(categoryId, page, event, scheduledDateOverride);
  };

  const dropOnWeekdayFromView = (categoryId, scheduledDate, event) => {
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
    if (target?.closest?.(".ProseMirror") || target?.closest?.("input, textarea, select, button")) {
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
