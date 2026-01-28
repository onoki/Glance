import { computed, nextTick, ref } from "vue";
import {
  completeTask,
  createTask as createTaskApi,
  deleteTask as deleteTaskApi,
  fetchChanges,
  fetchDashboard,
  runRecurrence,
  updateTask as updateTaskApi
} from "../api/tasks.js";
import { runDailyMaintenance } from "../api/maintenance.js";
import { deriveCategories } from "../utils/categoryUtils.js";
import { formatDateKey, getDayKey, getWeekStart } from "../utils/dateUtils.js";
import { DASHBOARD_MAIN_PAGE, DASHBOARD_NEW_PAGE } from "../utils/pageConstants.js";
import {
  emptyContentDoc,
  emptyTitleDoc,
  normalizeContent,
  normalizeTask,
  normalizeTitle,
  titleDocToListItem
} from "../utils/taskUtils.js";
import { shouldDeleteEmptyOnComplete } from "../utils/taskCompletionUtils.js";

export const useDashboardData = (options) => {
  const activeTab = options?.activeTab;
  const loadHistory = options?.loadHistory;
  const loadMaintenanceStatus = options?.loadMaintenanceStatus;
  const loadWarnings = options?.loadWarnings;

  const newTasks = ref([]);
  const mainTasks = ref([]);
  const expandedNew = ref(false);
  const lastChangeId = ref(0);
  const focusTaskId = ref(null);
  const focusContentTarget = ref(null);
  const currentDayKey = ref("");
  const creatingDefaultNew = ref(false);

  const dirtySnapshots = new Map();
  const recurrenceCache = new Map();
  const scrollTargetId = ref(null);

  const mainCategories = computed(() => deriveCategories(mainTasks.value));

  const mergeTasks = (tasks, page) => {
    const merged = tasks.map((task) => {
      const snapshot = dirtySnapshots.get(task.id);
      return snapshot && snapshot.dirty ? snapshot.task : task;
    });

    for (const [id, snapshot] of dirtySnapshots.entries()) {
      if (!snapshot.dirty || snapshot.task.page !== page) {
        continue;
      }
      if (!merged.some((task) => task.id === id)) {
        merged.push(snapshot.task);
      }
    }

    return merged;
  };

  const loadDashboard = async () => {
    const data = await fetchDashboard();
    newTasks.value = mergeTasks(data.newTasks.map(normalizeTask), DASHBOARD_NEW_PAGE);
    mainTasks.value = mergeTasks(data.mainTasks.map(normalizeTask), DASHBOARD_MAIN_PAGE);
    if (newTasks.value.length === 0 && creatingDefaultNew.value) {
      creatingDefaultNew.value = false;
    }
    if (scrollTargetId.value) {
      await nextTick();
      await new Promise((resolve) => requestAnimationFrame(resolve));
      const target = scrollTargetId.value;
      const element = document.querySelector(`[data-task-id="${target}"]`);
      if (element) {
        element.scrollIntoView({ block: "nearest", behavior: "smooth" });
        const container = element.closest(".list-card");
        if (container) {
          const containerRect = container.getBoundingClientRect();
          const elementRect = element.getBoundingClientRect();
          const padding = 12;
          if (elementRect.bottom > containerRect.bottom - padding) {
            const delta = elementRect.bottom - containerRect.bottom + padding;
            container.scrollTo({ top: container.scrollTop + delta, behavior: "smooth" });
          } else if (elementRect.top < containerRect.top + padding) {
            const delta = elementRect.top - containerRect.top - padding;
            container.scrollTo({ top: container.scrollTop + delta, behavior: "smooth" });
          }
        }
      }
      scrollTargetId.value = null;
    }
  };

  const insertTaskLocal = (task) => {
    const list = task.page === DASHBOARD_NEW_PAGE ? newTasks.value : mainTasks.value;
    const next = [...list, task].sort((a, b) => a.position - b.position);
    if (task.page === DASHBOARD_NEW_PAGE) {
      newTasks.value = next;
    } else {
      mainTasks.value = next;
    }
  };

  const buildCategoryUpdate = (task, categoryId) => {
    let scheduledDate = null;
    let recurrence = null;
    const cachedRecurrence = recurrenceCache.get(task.id);
    const existingRecurrence = task.recurrence;

    if (categoryId !== "repeatable" && existingRecurrence && existingRecurrence.type !== "notes") {
      recurrenceCache.set(task.id, existingRecurrence);
    }

    if (categoryId.startsWith("week-")) {
      const weekKey = categoryId.slice(5);
      const currentWeekKey = formatDateKey(getWeekStart(new Date()));
      scheduledDate = weekKey === currentWeekKey ? getDayKey() : weekKey;
      recurrence = null;
    } else {
      switch (categoryId) {
        case "uncategorized":
          scheduledDate = null;
          recurrence = null;
          break;
        case "notes":
          scheduledDate = null;
          recurrence = { type: "notes" };
          break;
        case "no-date":
          scheduledDate = "no-date";
          recurrence = null;
          break;
        case "repeatable":
          scheduledDate = null;
          recurrence = existingRecurrence && existingRecurrence.type !== "notes"
            ? existingRecurrence
            : cachedRecurrence || { type: "weekly", weekdays: [] };
          break;
        default:
          return null;
      }
    }

    return { scheduledDate, recurrence };
  };

  const createTask = async (page, titleOverride, contentOverride, positionOverride, categoryId = null) => {
    let scheduledDate = null;
    let recurrence = null;
    if (page === DASHBOARD_MAIN_PAGE && categoryId) {
      const categoryUpdate = buildCategoryUpdate({ id: "__new", recurrence: null }, categoryId);
      if (categoryUpdate) {
        scheduledDate = categoryUpdate.scheduledDate ?? null;
        recurrence = categoryUpdate.recurrence ?? null;
      }
    }
    const payload = {
      page,
      title: normalizeTitle(titleOverride ?? emptyTitleDoc()),
      content: normalizeContent(contentOverride || emptyContentDoc()),
      position: positionOverride || Date.now(),
      scheduledDate,
      recurrence
    };
    const response = await createTaskApi(payload);
    insertTaskLocal({
      id: response.taskId,
      page: payload.page,
      title: payload.title,
      content: payload.content,
      position: payload.position,
      createdAt: Date.now(),
      updatedAt: response.updatedAt,
      completedAt: null,
      scheduledDate: payload.scheduledDate,
      recurrence: payload.recurrence
    });
    await loadDashboard();
    return response.taskId;
  };

  const createTaskBelow = async (task, categoryId = null) => {
    const list = task.page === DASHBOARD_NEW_PAGE ? newTasks.value : mainTasks.value;
    const index = list.findIndex((item) => item.id === task.id);
    const next = index >= 0 ? list[index + 1] : null;
    const position = next ? (task.position + next.position) / 2 : task.position + 1;
    const newId = await createTask(task.page, emptyTitleDoc(), emptyContentDoc(), position, categoryId);
    focusTaskId.value = newId;
    return newId;
  };

  const updateTaskLocal = (id, patch) => {
    const apply = (list) => {
      const task = list.find((item) => item.id === id);
      if (task) {
        Object.assign(task, patch);
      }
    };
    apply(newTasks.value);
    apply(mainTasks.value);
  };

  const saveTask = async ({ id, title, content, baseUpdatedAt, page }) => {
    const response = await updateTaskApi(id, {
      baseUpdatedAt,
      title: normalizeTitle(title),
      content: normalizeContent(content),
      page
    });
    updateTaskLocal(id, { title, content, updatedAt: response.updatedAt });
    await loadDashboard();
  };

  const deleteTask = async (task) => {
    if (!task?.id) {
      return;
    }
    const list = getOrderedTasksForPage(task);
    const index = list.findIndex((item) => item.id === task.id);
    const prevTask = index > 0 ? list[index - 1] : null;
    try {
      await deleteTaskApi(task.id);
    } catch {
      return;
    }
    dirtySnapshots.delete(task.id);
    newTasks.value = newTasks.value.filter((item) => item.id !== task.id);
    mainTasks.value = mainTasks.value.filter((item) => item.id !== task.id);
    if (prevTask) {
      focusTaskId.value = prevTask.id;
    }
    await loadDashboard();
    if (activeTab?.value === "History") {
      await loadHistory?.();
    }
  };

  const toggleComplete = async (task) => {
    if (shouldDeleteEmptyOnComplete(task)) {
      await deleteTask(task);
      return;
    }
    const completed = !task.completedAt;
    task.completedAt = completed ? Date.now() : null;
    await completeTask(task.id, { completed });
    await loadDashboard();
    if (activeTab?.value === "History") {
      await loadHistory?.();
    }
  };

  const moveNewToMain = async () => {
    const updates = newTasks.value.map((task) =>
      updateTaskApi(task.id, {
        baseUpdatedAt: task.updatedAt,
        title: task.title,
        content: task.content,
        page: DASHBOARD_MAIN_PAGE
      })
    );
    await Promise.all(updates);
    await loadDashboard();
  };

  const getOrderedTasksForPage = (task) => {
    if (task.page === DASHBOARD_NEW_PAGE) {
      return newTasks.value;
    }
    if (task.page === DASHBOARD_MAIN_PAGE) {
      return mainCategories.value.flatMap((category) => category.tasks);
    }
    return mainTasks.value;
  };

  const moveTaskToPrevious = async (task) => {
    const list = task.page === DASHBOARD_NEW_PAGE ? newTasks.value : mainTasks.value;
    const index = list.findIndex((item) => item.id === task.id);
    if (index <= 0) {
      return false;
    }

    const previous = list[index - 1];
    const prevContent = normalizeContent(previous.content);
    const prevList = prevContent.content?.[0]?.content ?? [];
    const currentContent = normalizeContent(task.content);
    const currentList = currentContent.content?.[0]?.content ?? [];
    const titleItem = titleDocToListItem(task.title);

    const merged = {
      type: "doc",
      content: [
        {
          type: "bulletList",
          content: [...prevList, titleItem, ...currentList]
        }
      ]
    };

    await saveTask({
      id: previous.id,
      title: previous.title,
      content: merged,
      baseUpdatedAt: previous.updatedAt,
      page: previous.page
    });

    await saveTask({
      id: task.id,
      title: task.title || "Untitled",
      content: currentContent,
      baseUpdatedAt: task.updatedAt,
      page: "dashboard:hidden"
    });

    focusContentTarget.value = {
      taskId: previous.id,
      listIndex: prevList.length,
      atEnd: true
    };

    newTasks.value = newTasks.value.filter((item) => item.id !== task.id);
    mainTasks.value = mainTasks.value.filter((item) => item.id !== task.id);

    return true;
  };

  const focusPrevTaskFromTitle = async (task) => {
    const list = getOrderedTasksForPage(task);
    const index = list.findIndex((item) => item.id === task.id);
    if (index <= 0) {
      return;
    }
    const previous = list[index - 1];
    const prevContent = normalizeContent(previous.content);
    const prevList = prevContent.content?.[0]?.content ?? [];
    if (prevList.length === 0) {
      focusTaskId.value = previous.id;
      return;
    }
    const listIndex = Math.max(prevList.length - 1, 0);
    focusContentTarget.value = {
      taskId: previous.id,
      listIndex,
      atEnd: true
    };
  };

  const focusNextTaskFromContent = async (task) => {
    const list = getOrderedTasksForPage(task);
    const index = list.findIndex((item) => item.id === task.id);
    const next = index >= 0 ? list[index + 1] : null;
    if (next) {
      focusTaskId.value = next.id;
      return;
    }
    const newId = await createTaskBelow(task);
    focusTaskId.value = newId;
  };

  const splitSubcontentToNewTask = async (task, payload) => {
    const categoryId = payload?.categoryId ?? null;
    const list = task.page === DASHBOARD_NEW_PAGE ? newTasks.value : mainTasks.value;
    const index = list.findIndex((item) => item.id === task.id);
    const next = index >= 0 ? list[index + 1] : null;
    const position = next ? (task.position + next.position) / 2 : task.position + 1;
    const newId = await createTask(task.page, payload.title, payload.content, position, categoryId);
    focusTaskId.value = newId;
  };

  const handleDirtyChange = (id, dirty, snapshot) => {
    if (!id) {
      return;
    }
    if (!dirty) {
      dirtySnapshots.delete(id);
      return;
    }
    dirtySnapshots.set(id, { dirty: true, task: snapshot });
  };

  const runRecurrenceGeneration = async () => {
    try {
    await runRecurrence();
    } catch {
      // ignore failures; dashboard refresh will retry later
    }
  };

  const runDailyMaintenance = async () => {
    try {
      await runDailyMaintenance();
      await loadMaintenanceStatus?.();
    } catch {
      // ignore failures
    }
  };

  const setTaskCategory = async (task, category) => {
    let scheduledDate = undefined;
    let recurrence = undefined;
    const weekStart = getWeekStart(new Date());
    const cachedRecurrence = recurrenceCache.get(task.id);
    const existingRecurrence = task.recurrence;

    if (category !== "repeatable" && existingRecurrence && existingRecurrence.type !== "notes") {
      recurrenceCache.set(task.id, existingRecurrence);
    }

    switch (category) {
      case "uncategorized":
        scheduledDate = null;
        recurrence = null;
        break;
      case "notes":
        scheduledDate = null;
        recurrence = { type: "notes" };
        break;
      case "no-date":
        scheduledDate = "no-date";
        recurrence = null;
        break;
      case "this-week":
        scheduledDate = getDayKey();
        recurrence = null;
        break;
      case "next-week": {
        const nextWeek = new Date(weekStart);
        nextWeek.setDate(weekStart.getDate() + 7);
        scheduledDate = formatDateKey(nextWeek);
        recurrence = null;
        break;
      }
      case "repeatable":
        scheduledDate = null;
        recurrence = existingRecurrence && existingRecurrence.type !== "notes"
          ? existingRecurrence
          : cachedRecurrence || { type: "weekly", weekdays: [] };
        break;
      default:
        return;
    }

    const update = {
      baseUpdatedAt: task.updatedAt,
      scheduledDate,
      recurrence
    };
    if (task.page === DASHBOARD_NEW_PAGE) {
      update.page = DASHBOARD_MAIN_PAGE;
    }
    await updateTaskApi(task.id, update);
    if (recurrence) {
      await runRecurrenceGeneration();
    }
    scrollTargetId.value = task.id;
    await loadDashboard();
  };

  const setTaskRecurrence = async (task, recurrence) => {
    if (recurrence) {
      recurrenceCache.set(task.id, recurrence);
    }
    await updateTaskApi(task.id, {
      baseUpdatedAt: task.updatedAt,
      recurrence
    });
    await runRecurrenceGeneration();
    await loadDashboard();
  };

  const buildCategoryUpdateForMove = (task, categoryId) => {
    if (task.page === DASHBOARD_MAIN_PAGE) {
      return buildCategoryUpdate(task, categoryId);
    }
    return null;
  };

  const applyTaskMove = async (task, categoryId, position, scheduledDateOverride = null) => {
    const update = {
      baseUpdatedAt: task.updatedAt,
      position
    };

    const categoryUpdate = buildCategoryUpdateForMove(task, categoryId);
    if (categoryUpdate) {
      update.scheduledDate = categoryUpdate.scheduledDate;
      update.recurrence = categoryUpdate.recurrence;
    }
    if (scheduledDateOverride) {
      update.scheduledDate = scheduledDateOverride;
    }

    await updateTaskApi(task.id, update);
    if (update.recurrence) {
      await runRecurrenceGeneration();
    }
    scrollTargetId.value = task.id;
    await loadDashboard();
  };

  const findTaskById = (id) => {
    return [...newTasks.value, ...mainTasks.value].find((task) => task.id === id) || null;
  };

  const getCategoryTasks = (page, categoryId) => {
    if (page === DASHBOARD_NEW_PAGE) {
      return newTasks.value;
    }
    if (page === DASHBOARD_MAIN_PAGE) {
      const category = mainCategories.value.find((item) => item.id === categoryId);
      return category ? category.tasks : [];
    }
    return [];
  };

  const pollChanges = async () => {
    const previous = lastChangeId.value;
    const data = await fetchChanges(previous);
    lastChangeId.value = data.lastId;
    if (data.lastId > previous) {
      try {
        await loadDashboard();
      } catch {
        // keep going so history refresh can still run
      }
      if (activeTab?.value === "History") {
        await loadHistory?.();
      }
    }
  };

  const handleDayTick = async () => {
    const nextDay = getDayKey();
    if (nextDay !== currentDayKey.value) {
      currentDayKey.value = nextDay;
      await runRecurrenceGeneration();
      await runDailyMaintenance();
      await loadDashboard();
      if (activeTab?.value === "History") {
        await loadHistory?.();
      }
      await loadWarnings?.();
    }
  };

  const initDayKey = () => {
    currentDayKey.value = getDayKey();
  };

  return {
    newTasks,
    mainTasks,
    expandedNew,
    focusTaskId,
    focusContentTarget,
    mainCategories,
    loadDashboard,
    createTask,
    createTaskBelow,
    saveTask,
    deleteTask,
    toggleComplete,
    moveNewToMain,
    setTaskCategory,
    setTaskRecurrence,
    splitSubcontentToNewTask,
    moveTaskToPrevious,
    focusPrevTaskFromTitle,
    focusNextTaskFromContent,
    handleDirtyChange,
    applyTaskMove,
    findTaskById,
    getCategoryTasks,
    getOrderedTasksForPage,
    pollChanges,
    handleDayTick,
    initDayKey,
    currentDayKey
  };
};
