import { applyTaskHistory, taskRevision } from "../utils/taskHistory.js";
import { taskActionChanges, taskActionUpdates } from "../utils/taskActions.js";
import { detachBeforeMove, detachTaskGroups, incompleteTaskGroup, taskGroupChanges, taskInsertionPosition } from "../utils/taskGroups.js";
import { horizontalTaskTarget, TASK_HIGHLIGHT_MS } from "../utils/taskNavigation.js";
import { indentTaskDocuments, joinTaskDocuments, createTaskActionQueue } from "../utils/taskJoin.js";
import { saveCoordinator } from "../services/saveCoordinator.js";
import { announceAction } from "../services/actionFeedback.js";
import { computed, nextTick, ref } from "vue";
import {
  archivePreviousCompletions,
  completeTask,
  createTask as createTaskApi,
  deleteTask as deleteTaskApi,
  fetchChanges,
  fetchDashboard,
  restoreTask as restoreTaskApi,
  runRecurrence,
  updateTask as updateTaskApi,
  updateTasks
} from "../api/tasks.js";
import { planningWeekDays, relativeWeekStart, deriveCategories, categoryWeekGroups, isPlanningWeekCategory } from "../utils/categoryUtils.js";
import { formatDateKey, getDayKey, getWeekStart, parseDateKey } from "../utils/dateUtils.js";
import { DASHBOARD_MAIN_PAGE, DASHBOARD_NEW_PAGE } from "../utils/pageConstants.js";
import {
  emptyContentDoc,
  emptyTitleDoc,
  normalizeContent,
  normalizeTask,
  normalizeTitle
} from "../utils/taskUtils.js";
import { shouldDeleteEmptyOnComplete } from "../utils/taskCompletionUtils.js";
import { runDailyMaintenance as runDailyMaintenanceApi } from "../api/maintenance.js";

export const useDashboardData = (options) => {
  const activeTab = options?.activeTab;
  const loadHistory = options?.loadHistory;
  const loadMaintenanceStatus = options?.loadMaintenanceStatus;
  const loadWarnings = options?.loadWarnings;
  const dashboardColumnsRef = options?.dashboardColumnsRef;

  const newTasks = ref([]);
  const mainTasks = ref([]);
  const maximizedCategoryId = ref(null);
  const lastChangeId = ref(0);
  const focusTaskId = ref(null);
  const focusContentTarget = ref(null);
  const currentDayKey = ref("");
  const creatingDefaultNew = ref(false);

  const dirtySnapshots = new Map();
  const recurrenceCache = new Map();
  const scrollTargetId = ref(null);
  const highlightTaskId = ref(null);
  const highlightNonce = ref(0);
  let highlightTimer = null;

  const mainCategories = computed(() => deriveCategories(mainTasks.value));

  const undoStack = ref([]);
  const redoStack = ref([]);
  const undoLimit = 100;
  let isApplyingUndo = false;
  let suppressUndoCount = 0;
  const undoSignal = ref(0);
  const queueTaskAction = createTaskActionQueue();
  const logicalToActual = new Map();
  const actualToLogical = new Map();

  const registerTaskId = (actualId) => {
    if (!actualToLogical.has(actualId)) {
      actualToLogical.set(actualId, actualId);
      logicalToActual.set(actualId, actualId);
    }
    return actualToLogical.get(actualId);
  };

  const resolveLogicalId = (actualId) => actualToLogical.get(actualId) ?? registerTaskId(actualId);

  const resolveActualId = (logicalId) => {
    if (logicalToActual.has(logicalId)) {
      return logicalToActual.get(logicalId);
    }
    return logicalId;
  };

  const updateMappingForCreate = (logicalId, actualId) => {
    const previousActual = logicalToActual.get(logicalId);
    if (previousActual && actualToLogical.get(previousActual) === logicalId) {
      actualToLogical.delete(previousActual);
    }
    logicalToActual.set(logicalId, actualId);
    actualToLogical.set(actualId, logicalId);
  };

  const markLogicalDeleted = (logicalId, actualId) => {
    if (actualId) {
      logicalToActual.set(logicalId, actualId);
      actualToLogical.set(actualId, logicalId);
    }
  };

  const snapshotTask = (task) => ({
    page: task.page,
    title: normalizeTitle(task.title),
    content: normalizeContent(task.content),
    position: task.position,
    scheduledDate: task.scheduledDate ?? null,
    recurrence: task.recurrence ?? null,
    ownerPersonId: task.ownerPersonId ?? null,
    completedAt: task.completedAt ?? null,
    isPrivate: !!task.isPrivate,
    groupId: task.groupId ?? null
  });

  const snapshotFromPayload = (payload) => ({
    page: payload.page,
    title: normalizeTitle(payload.title),
    content: normalizeContent(payload.content),
    position: payload.position,
    scheduledDate: payload.scheduledDate ?? null,
    recurrence: payload.recurrence ?? null,
    isPrivate: !!payload.isPrivate,
    groupId: null
  });

  const snapshotsEqual = (left, right) => JSON.stringify(left) === JSON.stringify(right);

  const pushUndoEntry = (entry) => {
    if (isApplyingUndo || suppressUndoCount > 0) {
      return;
    }
    undoStack.value = [...undoStack.value, entry];
    if (undoStack.value.length > undoLimit) {
      undoStack.value.shift();
    }
    redoStack.value = [];
  };

  const withUndoSuppressed = async (action) => {
    suppressUndoCount += 1;
    try {
      return await action();
    } finally {
      suppressUndoCount = Math.max(0, suppressUndoCount - 1);
    }
  };

  const resolveTaskSnapshot = (candidate) => {
    const snapshot = candidate ? dirtySnapshots.get(candidate.id) : null;
    return snapshot && snapshot.dirty ? snapshot.task : candidate;
  };

  const getStartOfToday = () => {
    const now = new Date();
    now.setHours(0, 0, 0, 0);
    return now.getTime();
  };

  const isTaskVisibleToday = (task, cutoff) => !task.completedAt || (task.completedAt >= cutoff && !task.historyArchivedAt);

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

  const triggerHighlight = (taskId) => {
    if (!taskId) {
      return;
    }
    highlightTaskId.value = taskId;
    highlightNonce.value += 1;
    clearTimeout(highlightTimer);
    highlightTimer = setTimeout(() => { highlightTaskId.value = null; }, TASK_HIGHLIGHT_MS);
  };

  const findCategoryIdForTask = (id) => {
    if (newTasks.value.some((task) => task.id === id)) {
      return "new";
    }
    for (const category of mainCategories.value) {
      if (category.tasks.some((task) => task.id === id)) {
        return category.id;
      }
    }
    return null;
  };

  const getStoredTaskById = (id) =>
    newTasks.value.find((task) => task.id === id) || mainTasks.value.find((task) => task.id === id);

  const loadDashboard = async () => {
    const data = await fetchDashboard();
    const cutoff = getStartOfToday();
    newTasks.value = mergeTasks(data.newTasks.map(normalizeTask), DASHBOARD_NEW_PAGE)
      .filter((task) => isTaskVisibleToday(task, cutoff));
    mainTasks.value = mergeTasks(data.mainTasks.map(normalizeTask), DASHBOARD_MAIN_PAGE)
      .filter((task) => isTaskVisibleToday(task, cutoff));
    [...newTasks.value, ...mainTasks.value].forEach((task) => registerTaskId(task.id));
    if (newTasks.value.length === 0 && creatingDefaultNew.value) {
      creatingDefaultNew.value = false;
    }
    if (scrollTargetId.value) {
      const target = scrollTargetId.value;
      const targetCategoryId = findCategoryIdForTask(target);
      if (maximizedCategoryId.value && targetCategoryId && maximizedCategoryId.value !== targetCategoryId) maximizedCategoryId.value = null;
      await nextTick();
      await new Promise((resolve) => requestAnimationFrame(resolve));
      const columnSelector = targetCategoryId
        ? `.dashboard-column[data-category-id="${targetCategoryId}"]`
        : null;
      const columnElement = columnSelector ? document.querySelector(columnSelector) : null;
      const elements = Array.from(document.querySelectorAll(`[data-task-id="${target}"]`));
      const element = columnElement
        ? columnElement.querySelector(`[data-task-id="${target}"]`)
        : elements[0] || null;
      if (element) {
        const padding = 12;
        const container = columnElement?.querySelector(".list-card") || element.closest(".list-card");
        const columns = dashboardColumnsRef?.value;
        const column = element.closest(".dashboard-column");
        const isScrollable = (node, axis) => {
          if (!node) {
            return false;
          }
          const style = window.getComputedStyle(node);
          const overflow = axis === "y" ? style.overflowY : style.overflowX;
          const isScrollableOverflow = overflow === "auto" || overflow === "scroll" || overflow === "overlay";
          if (!isScrollableOverflow) {
            return false;
          }
          const size = axis === "y"
            ? node.scrollHeight - node.clientHeight
            : node.scrollWidth - node.clientWidth;
          return size > 1;
        };
        const findScrollableAncestor = (node, axis) => {
          let current = node?.parentElement;
          while (current && current !== document.body) {
            if (isScrollable(current, axis)) {
              return current;
            }
            current = current.parentElement;
          }
          return null;
        };
        const getVerticalContainer = () => {
          if (container) {
            const taskList = container.querySelector(".task-list");
            if (isScrollable(container, "y")) {
              return container;
            }
            if (isScrollable(taskList, "y")) {
              return taskList;
            }
          }
          return findScrollableAncestor(element, "y") || container;
        };
        const getHorizontalContainer = () => {
          if (isScrollable(columns, "x")) {
            return columns;
          }
          return findScrollableAncestor(element, "x") || columns;
        };
        const adjustVertical = (behavior) => {
          const verticalContainer = getVerticalContainer();
          if (!verticalContainer) {
            return;
          }
          const containerRect = verticalContainer.getBoundingClientRect();
          const elementRect = element.getBoundingClientRect();
          const maxScrollTop = verticalContainer.scrollHeight - verticalContainer.clientHeight;
          if (maxScrollTop <= 0) {
            return;
          }
          const relativeTop = verticalContainer.scrollTop + (elementRect.top - containerRect.top);
          const relativeBottom = relativeTop + elementRect.height;
          let nextTop = null;
          if (elementRect.top < containerRect.top + padding) {
            nextTop = Math.max(0, relativeTop - padding);
          } else if (elementRect.bottom > containerRect.bottom - padding) {
            nextTop = Math.min(maxScrollTop, relativeBottom - verticalContainer.clientHeight + padding);
          }
          if (nextTop !== null && nextTop !== verticalContainer.scrollTop) {
            verticalContainer.scrollTo({ top: nextTop, behavior });
          }
        };
        const adjustHorizontal = (behavior) => {
          const horizontalContainer = getHorizontalContainer();
          if (!horizontalContainer || !column) {
            return;
          }
          const columnsRect = horizontalContainer.getBoundingClientRect();
          const columnRect = column.getBoundingClientRect();
          const maxScrollLeft = horizontalContainer.scrollWidth - horizontalContainer.clientWidth;
          if (maxScrollLeft <= 0) {
            return;
          }
          const relativeLeft = horizontalContainer.scrollLeft + (columnRect.left - columnsRect.left);
          const relativeRight = relativeLeft + columnRect.width;
          let nextLeft = null;
          if (columnRect.left < columnsRect.left + padding) {
            nextLeft = Math.max(0, relativeLeft - padding);
          } else if (columnRect.right > columnsRect.right - padding) {
            nextLeft = Math.min(maxScrollLeft, relativeRight - horizontalContainer.clientWidth + padding);
          }
          if (nextLeft !== null && nextLeft !== horizontalContainer.scrollLeft) {
            horizontalContainer.scrollTo({ left: nextLeft, behavior });
          }
        };

        element.scrollIntoView({ block: "nearest", inline: "nearest", behavior: "smooth" });
        adjustVertical("smooth");
        adjustHorizontal("smooth");

        requestAnimationFrame(() => {
          adjustVertical("auto");
          adjustHorizontal("auto");
          triggerHighlight(target);
        });
      }
      scrollTargetId.value = null;
    }
  };

  const navigateToTask = async (taskId) => {
    if (!taskId) {
      return false;
    }
    scrollTargetId.value = taskId;
    await loadDashboard();
    return !!getStoredTaskById(taskId);
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

    if (categoryId.startsWith("day-")) {
      const day = planningWeekDays().find(day => day.dateKey === categoryId.slice(4) && !day.isPast);
      if (!day) throw new Error("Choose today or a remaining day of this or next week.");
      return { scheduledDate: day.dateKey, recurrence: null };
    }
    if (categoryId.startsWith("week-")) {
      const weekKey = categoryId.slice(5);
      const currentWeekKey = formatDateKey(getWeekStart(new Date()));
      scheduledDate = weekKey === currentWeekKey ? getDayKey() : weekKey;
      recurrence = null;
    } else {
      switch (categoryId) {
        case "two-weeks":
          scheduledDate = formatDateKey(relativeWeekStart(2));
          break;
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

  const createTaskInternal = async (payload, options = {}) => {
    const response = await createTaskApi(payload);
    const created = {
      id: response.taskId,
      page: payload.page,
      title: payload.title,
      content: payload.content,
      position: payload.position,
      createdAt: Date.now(),
      updatedAt: response.updatedAt,
      completedAt: response.completedAt ?? null,
      scheduledDate: payload.scheduledDate,
      recurrence: payload.recurrence,
      isPrivate: !!payload.isPrivate
    };
    insertTaskLocal(created);
    if (options.logicalId) {
      updateMappingForCreate(options.logicalId, response.taskId);
    } else {
      registerTaskId(response.taskId);
    }
    if (!options.suppressUndo) {
      const logicalId = options.logicalId ?? resolveLogicalId(response.taskId);
      pushUndoEntry({
        label: "create",
        diffs: [{ logicalId, before: null, after: snapshotFromPayload(payload) }]
      });
    }
    await loadDashboard();
    return response.taskId;
  };

  const createTask = async (
    page,
    titleOverride,
    contentOverride,
    positionOverride,
    categoryId = null,
    options = {}
  ) => {
    let scheduledDate = null;
    let recurrence = null;
    if (page === DASHBOARD_MAIN_PAGE && categoryId) {
      const categoryUpdate = buildCategoryUpdate({ id: "__new", recurrence: null }, categoryId);
      if (categoryUpdate) {
        scheduledDate = categoryUpdate.scheduledDate ?? null;
        recurrence = categoryUpdate.recurrence ?? null;
      }
    }
    // Editing within an existing scheduled task keeps its date. Category moves
    // and independent additions still use their usual default date.
    const source = options.scheduleFrom;
    if (page === DASHBOARD_MAIN_PAGE && source?.scheduledDate && !source.recurrence
      && parseDateKey(source.scheduledDate) && (!categoryId || categoryId.startsWith("week-"))) {
      scheduledDate = source.scheduledDate;
    }
    const payload = {
      page,
      title: normalizeTitle(titleOverride ?? emptyTitleDoc()),
      content: normalizeContent(contentOverride || emptyContentDoc()),
      position: positionOverride ?? Date.now(),
      scheduledDate,
      recurrence,
      isPrivate: !!source?.isPrivate,
      ...(options.completed ? { completed: true } : {})
    };
    return createTaskInternal(payload, options);
  };

  const createTaskBelow = queueTaskAction(async (task, categoryId = null, overrides = null, options = {}) => {
    if (!(await saveCoordinator.flushAll()).ok) return null;
    task = getStoredTaskById(task.id) || task;
    const position = taskInsertionPosition(taskGroupRows(task), task, options.before);
    const title = overrides?.title ?? emptyTitleDoc();
    const content = overrides?.content ?? emptyContentDoc();
    const newId = await createTask(task.page, title, content, position, categoryId, { ...options, scheduleFrom: options.inheritScheduledDate === false ? null : task });
    focusTaskId.value = options.before ? { taskId: task.id, selection: { from: 1, to: 1 } } : newId;
    return newId;
  });

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

  const saveTask = async ({ id, title, content, baseUpdatedAt, page, isPrivate, suppressUndo = false }) => {
    const stored = getStoredTaskById(id);
    const logicalId = stored ? resolveLogicalId(stored.id) : resolveLogicalId(id);
    const beforeSnapshot = stored ? snapshotTask(stored) : null;
    const privateFlag = isPrivate ?? stored?.isPrivate ?? false;
    const normalizedTitle = normalizeTitle(title);
    const normalizedContent = normalizeContent(content);
    const afterSnapshot = beforeSnapshot
      ? { ...beforeSnapshot, title: normalizedTitle, content: normalizedContent, isPrivate: privateFlag }
      : {
          page: page ?? stored?.page ?? DASHBOARD_MAIN_PAGE,
          title: normalizedTitle,
          content: normalizedContent,
          position: stored?.position ?? Date.now(),
          scheduledDate: stored?.scheduledDate ?? null,
          recurrence: stored?.recurrence ?? null
        };

    const response = await updateTaskApi(id, {
      baseUpdatedAt,
      title: normalizedTitle,
      content: normalizedContent,
      isPrivate: privateFlag,
      page
    });
    if (!suppressUndo && beforeSnapshot && !snapshotsEqual(beforeSnapshot, afterSnapshot)) {
      pushUndoEntry({
        label: "edit",
        diffs: [{ logicalId, before: beforeSnapshot, after: afterSnapshot }]
      });
    }

    updateTaskLocal(id, { title: normalizedTitle, content: normalizedContent, isPrivate: privateFlag, updatedAt: response.updatedAt });
    // The refresh below keeps dirty editor snapshots. Advance only their revision
    // after our acknowledged write, so structural edits cannot reuse the old one.
    const dirtySnapshot = dirtySnapshots.get(id);
    if (dirtySnapshot) dirtySnapshot.task.updatedAt = response.updatedAt;
    await loadDashboard();
    return response;
  };

  const deleteTask = queueTaskAction(async (task, options = {}) => {
    if (!(await saveCoordinator.flushAll()).ok) throw new Error("Could not save edits before deleting the note.");
    if (!task?.id) {
      return;
    }
    const snapshot = resolveTaskSnapshot(task);
    if (dirtySnapshots.get(task.id)?.dirty) {
      const saved = await saveTask({ ...snapshot, baseUpdatedAt: taskRevision(snapshot), suppressUndo: true });
      snapshot.updatedAt = saved.updatedAt;
    }
    const logicalId = snapshot ? resolveLogicalId(snapshot.id) : resolveLogicalId(task.id);
    const beforeSnapshot = snapshot ? snapshotTask(snapshot) : null;
    const list = getOrderedTasksForPage(task);
    const index = list.findIndex((item) => item.id === task.id);
    const prevTask = index > 0 ? list[index - 1] : null;
    const nextTask = list[index + 1];
    await deleteTaskApi(task.id, taskRevision(snapshot));
    saveCoordinator.forget(task.id);
    if (!options.suppressUndo && beforeSnapshot) {
      pushUndoEntry({
        label: "delete",
        diffs: [{ logicalId, before: beforeSnapshot, after: null }]
      });
    }
    markLogicalDeleted(logicalId, task.id);
    dirtySnapshots.delete(task.id);
    newTasks.value = newTasks.value.filter((item) => item.id !== task.id);
    mainTasks.value = mainTasks.value.filter((item) => item.id !== task.id);
    focusTaskId.value = options.direction === "forward" && nextTask
      ? { taskId: nextTask.id, selection: { from: 1, to: 1 } } : prevTask?.id || null;
    await loadDashboard();
    if (activeTab?.value === "History") {
      await loadHistory?.();
    }
  });

  const toggleComplete = async (task) => {
    if (shouldDeleteEmptyOnComplete(task)) {
      await deleteTask(task);
    saveCoordinator.forget(task);
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

  const moveNewToMain = queueTaskAction(async () => {
    if (!(await saveCoordinator.flushAll()).ok) throw new Error("Could not save edits before moving the tasks.");
    const tasks = newTasks.value;
    if (!tasks.length) return;
    const grouped = tasks.some(task => task.groupId);
    const lastPosition = mainTasks.value.reduce((last, task) => Math.max(last, task.position), 0);
    const updates = tasks.map((task, index) => ({
      baseUpdatedAt: taskRevision(task),
      page: DASHBOARD_MAIN_PAGE,
      ...(grouped ? { position: lastPosition + index + 1 } : {})
    }));
    return persistTaskMoves(tasks, updates, null);
  });

  const getOrderedTasksForPage = (task) => {
    if (task.page === DASHBOARD_NEW_PAGE) {
      return newTasks.value;
    }
    if (task.page === DASHBOARD_MAIN_PAGE) {
      return mainCategories.value.flatMap(category => isPlanningWeekCategory(category)
        ? categoryWeekGroups(category).flatMap(group => group.tasks) : category.tasks);
    }
    return mainTasks.value;
  };

  const taskEditingList = (task) => {
    const category = mainCategories.value.find(item => item.tasks.some(row => row.id === task.id));
    return task.page === DASHBOARD_NEW_PAGE ? newTasks.value
      : category ? (isPlanningWeekCategory(category) ? categoryWeekGroups(category).flatMap(group => group.tasks) : category.tasks) : [];
  };

  const taskGroupRows = (task) => {
    if (!task) return [];
    const category = mainCategories.value.find(item => item.tasks.some(row => row.id === task.id));
    // A new adjacent row inherits the source date, so the position must be
    // between rows of that visible day rather than between different groups.
    return category && isPlanningWeekCategory(category)
      ? category.tasks.filter(row => row.scheduledDate === task.scheduledDate)
      : taskEditingList(task);
  };

  const moveTaskToPrevious = queueTaskAction(async (task) => {
    if (!(await saveCoordinator.flushAll()).ok) return null;
    const list = taskEditingList(task);
    const index = list.findIndex((item) => item.id === task.id);
    if (index <= 0) {
      return false;
    }

    const previous = resolveTaskSnapshot(list[index - 1]);
    const current = resolveTaskSnapshot(list[index]);
    const { content: merged, listIndex } = indentTaskDocuments(previous, current);

    const prevLogicalId = resolveLogicalId(previous.id);
    const currentLogicalId = resolveLogicalId(current.id);
    const beforePrevSnapshot = snapshotTask(previous);
    const beforeCurrentSnapshot = snapshotTask(current);
    const afterPrevSnapshot = { ...beforePrevSnapshot, content: merged, isPrivate: !!(previous.isPrivate || current.isPrivate) };
    const afterCurrentSnapshot = null;

    await saveTask({
      id: previous.id,
      title: previous.title,
      content: merged,
      isPrivate: !!(previous.isPrivate || current.isPrivate),
      baseUpdatedAt: previous.updatedAt,
      page: previous.page,
      suppressUndo: true
    });

    pushUndoEntry({
      label: "indent",
      diffs: [
        { logicalId: prevLogicalId, before: beforePrevSnapshot, after: afterPrevSnapshot },
        { logicalId: currentLogicalId, before: beforeCurrentSnapshot, after: afterCurrentSnapshot }
      ]
    });
    await deleteTaskApi(current.id, current.updatedAt);
    saveCoordinator.forget(current.id);

    focusContentTarget.value = {
      taskId: previous.id,
      listIndex,
      atEnd: true
    };

    newTasks.value = newTasks.value.filter((item) => item.id !== task.id);
    mainTasks.value = mainTasks.value.filter((item) => item.id !== task.id);

    return true;
  });

  const navigateHorizontal = (task, direction, vertical = null) => {
    const list = taskEditingList(task);
    const target = horizontalTaskTarget(list.map(resolveTaskSnapshot), task, direction);
    if (!target) return;
    if (vertical) target.vertical = { ...vertical, direction };
    focusTaskId.value = null;
    focusContentTarget.value = null;
    if (target.area === 'content') focusContentTarget.value = target;
    else focusTaskId.value = target;
  };

  const mergeTaskToPrevious = queueTaskAction(async (task, forward = false) => {
    if (!(await saveCoordinator.flushAll()).ok) return false;
    // Backspace joins visual neighbours in one column, not neighbours in the
    // server's interleaved list of every dashboard category.
    const list = taskEditingList(task);
    const index = list.findIndex((item) => item.id === task.id) + (forward ? 1 : 0);
    if (index <= 0 || index >= list.length) {
      return false;
    }

    const previous = resolveTaskSnapshot(list[index - 1]);
    const current = resolveTaskSnapshot(list[index]);
    if (!previous || !current) {
      return false;
    }

    const joined = joinTaskDocuments(previous, current);
    const mergedTitle = joined.title;
    const mergedContent = joined.content;
    const prevLogicalId = resolveLogicalId(previous.id);
    const currentLogicalId = resolveLogicalId(current.id);
    const beforePrevSnapshot = snapshotTask(previous);
    const beforeCurrentSnapshot = snapshotTask(current);
    const afterPrevSnapshot = { ...beforePrevSnapshot, title: mergedTitle, content: mergedContent, isPrivate: !!(previous.isPrivate || current.isPrivate) };
    const afterCurrentSnapshot = null;

    await saveTask({
      id: previous.id,
      title: mergedTitle,
      content: mergedContent,
      isPrivate: !!(previous.isPrivate || current.isPrivate),
      baseUpdatedAt: previous.updatedAt,
      page: previous.page,
      suppressUndo: true
    });

    pushUndoEntry({
      label: "merge",
      diffs: [
        { logicalId: prevLogicalId, before: beforePrevSnapshot, after: afterPrevSnapshot },
        { logicalId: currentLogicalId, before: beforeCurrentSnapshot, after: afterCurrentSnapshot }
      ]
    });
    await deleteTaskApi(current.id, current.updatedAt);
    saveCoordinator.forget(current.id);

    dirtySnapshots.delete(previous.id);
    dirtySnapshots.delete(current.id);
    focusTaskId.value = null;
    focusContentTarget.value = null;
    if (joined.area === 'content') focusContentTarget.value = { taskId: previous.id, selection: joined.selection };
    else focusTaskId.value = { taskId: previous.id, selection: joined.selection };

    newTasks.value = newTasks.value.filter((item) => item.id !== current.id);
    mainTasks.value = mainTasks.value.filter((item) => item.id !== current.id);

    return true;
  });

  const focusPrevTaskFromTitle = (task, intent) => navigateHorizontal(task, -1, intent);

  const focusNextTaskFromContent = (task, intent) => navigateHorizontal(task, 1, intent);

  const splitSubcontentToNewTask = (task, payload) => splitTitleToNewTask(task, payload?.categoryId, {
    beforeTitle: task.title, beforeContent: payload.previousContent,
    afterTitle: task.title, afterContent: payload.remainingContent,
    newTitle: payload.title, newContent: payload.content,
    titleSelection: payload.titleSelection, commitSource: payload.commitSource, completed: payload.completed
  });

  const splitTitleToNewTask = queueTaskAction(async (task, categoryId, payload) => {
    if (!(await saveCoordinator.flushAll()).ok) return null;
    task = getStoredTaskById(task.id) || task;
    const position = taskInsertionPosition(taskGroupRows(task), task);

    const snapshot = resolveTaskSnapshot(task);
    const logicalId = snapshot ? resolveLogicalId(snapshot.id) : resolveLogicalId(task.id);
    const beforeSnapshot = snapshot
      ? {
          ...snapshotTask(snapshot),
          title: payload?.beforeTitle ?? snapshot.title,
          content: payload?.beforeContent ?? snapshot.content
        }
      : null;
    const afterSnapshot = beforeSnapshot
      ? {
          ...beforeSnapshot,
          title: payload?.afterTitle ?? snapshot.title,
          content: payload?.afterContent ?? snapshot.content
        }
      : null;

    const newId = await createTask(
      task.page,
      payload?.newTitle ?? emptyTitleDoc(),
      payload?.newContent ?? emptyContentDoc(),
      position,
      categoryId,
      { suppressUndo: true, scheduleFrom: task, completed: payload?.completed }
    );
    const newTask = getStoredTaskById(newId);
    const newLogicalId = newTask ? resolveLogicalId(newTask.id) : resolveLogicalId(newId);
    const newAfterSnapshot = newTask ? snapshotTask(newTask) : snapshotFromPayload({
      page: task.page,
      title: payload?.newTitle ?? emptyTitleDoc(),
      content: payload?.newContent ?? emptyContentDoc(),
      position,
      scheduledDate: null,
      recurrence: null,
      completed: payload?.completed
    });

    try {
      if (payload?.commitSource && await payload.commitSource() === false) {
        throw new Error('The new note is saved, but the original could not be updated. Both versions have been kept.');
      }
    } catch (error) {
      // Only the copy was completed. Undo must not restore an old source over
      // later typing or a real conflicting write from another window.
      pushUndoEntry({ label: 'create', diffs: [{ logicalId: newLogicalId, before: null, after: newAfterSnapshot }] });
      throw error;
    }
    focusTaskId.value = payload?.titleSelection ? { taskId: newId, selection: payload.titleSelection } : newId;

    if (beforeSnapshot && afterSnapshot) {
      pushUndoEntry({
        label: "split",
        diffs: [
          { logicalId, before: beforeSnapshot, after: payload?.commitSource ? snapshotTask(getStoredTaskById(task.id)) : afterSnapshot },
          { logicalId: newLogicalId, before: null, after: newAfterSnapshot }
        ]
      });
    }
    return newId;
  });

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

  const applyUndoEntry = async (entry, direction) => {
    if (!(await saveCoordinator.flushAll()).ok) return false;
    if (entry?.clipboard) return entry.clipboard[direction]();
    if (!entry?.diffs?.length) return false;
    isApplyingUndo = true;
    try {
      return await withUndoSuppressed(() => applyTaskHistory(entry.diffs, direction, {
        fetchTasks: async () => {
          const data = await fetchDashboard();
          return [...data.newTasks, ...data.mainTasks].map(normalizeTask);
        },
        resolveId: diff => resolveActualId(diff.logicalId),
        restoreTask: restoreTaskApi, updateTask: updateTaskApi, updateTasks, deleteTask: deleteTaskApi,
        forget: id => saveCoordinator.forget(id)
      }));
    } finally {
      isApplyingUndo = false;
      await loadDashboard();
      undoSignal.value += 1;
    }
  };

  const undo = queueTaskAction(async () => {
    if (undoStack.value.length === 0) {
      return false;
    }
    const entry = undoStack.value[undoStack.value.length - 1];
    undoStack.value = undoStack.value.slice(0, -1);
    let applied;
    try { applied = await applyUndoEntry(entry, "undo"); }
    catch (error) { undoStack.value = [...undoStack.value, entry]; throw error; }
    if (!applied) undoStack.value = [...undoStack.value, entry];
    if (applied) {
      redoStack.value = [...redoStack.value, entry];
    }
    return applied;
  });

  const redo = queueTaskAction(async () => {
    if (redoStack.value.length === 0) {
      return false;
    }
    const entry = redoStack.value[redoStack.value.length - 1];
    redoStack.value = redoStack.value.slice(0, -1);
    let applied;
    try { applied = await applyUndoEntry(entry, "redo"); }
    catch (error) { redoStack.value = [...redoStack.value, entry]; throw error; }
    if (!applied) redoStack.value = [...redoStack.value, entry];
    if (applied) {
      undoStack.value = [...undoStack.value, entry];
    }
    return applied;
  });

  const runRecurrenceGeneration = async () => {
    try {
    await runRecurrence();
    } catch {
      // ignore failures; dashboard refresh will retry later
    }
  };

  const runDailyMaintenance = async () => {
    try {
      await runDailyMaintenanceApi();
      await loadMaintenanceStatus?.();
    } catch {
      // ignore failures
    }
  };

  const persistTaskMoves = async (tasks, updates, focusId, label = "move") => {
    const diffs = tasks.flatMap((task, index) => {
      const before = snapshotTask(task);
      const update = updates[index];
      const after = {
        ...before,
        page: update.page ?? before.page,
        position: update.position ?? before.position,
        scheduledDate: update.scheduledDate === undefined ? before.scheduledDate : update.scheduledDate,
        recurrence: update.recurrence === undefined ? before.recurrence : update.recurrence,
        groupId: update.groupId === undefined ? before.groupId : update.groupId
      };
      return snapshotsEqual(before, after) ? [] : [{ logicalId: resolveLogicalId(task.id), before, after }];
    });
    if (tasks.length === 1) {
      await updateTaskApi(tasks[0].id, updates[0]);
      if (updates[0].recurrence) await runRecurrenceGeneration();
    } else {
      await updateTasks(tasks.map((task, index) => ({ taskId: task.id, update: updates[index] })));
    }
    if (diffs.length) pushUndoEntry({ label, diffs });
    const entry = diffs.length ? undoStack.value.at(-1) : null;
    scrollTargetId.value = focusId;
    await loadDashboard();
    return entry;
  };

  const resolveMoveTasks = (tasks) => tasks.map(({ id }) => {
    const current = getStoredTaskById(id);
    if (!current) throw new Error("A selected task is no longer visible. Select the tasks again.");
    return current;
  });

  const changeTaskGroup = queueTaskAction(async (task, selectedIds, action) => {
    if (!(await saveCoordinator.flushAll()).ok) throw new Error("Could not save edits before changing the group.");
    const [current] = resolveMoveTasks([task]);
    const detachedIds = selectedIds.has(task.id) ? selectedIds : new Set([task.id]);
    const selectedLists = action === "detach"
      ? resolveMoveTasks([...detachedIds].map(id => ({ id }))).map(taskGroupRows) : [];
    const lists = new Map(selectedLists.map(rows => [rows[0]?.id, rows]));
    const changes = action === "detach" ? [...lists.values()].flatMap(rows => detachTaskGroups(rows, detachedIds))
      : taskGroupChanges(taskGroupRows(current), current, selectedIds, action);
    if (!changes.length) return false;
    const updates = changes.map(({ task: row, groupId, position }) => ({
      baseUpdatedAt: taskRevision(row), groupId, position
    }));
    return persistTaskMoves(changes.map(change => change.task), updates, null, "group");
  });

  const applyTaskAction = queueTaskAction(async (selection, action, value) => {
    if (!(await saveCoordinator.flushAll()).ok) throw new Error("Could not save edits before updating the selected tasks.");
    const tasks = resolveMoveTasks(selection);
    const changes = taskActionChanges(tasks, action, value);
    const diffs = changes.map(({ task, fields, deleted }) => ({
      logicalId: resolveLogicalId(task.id),
      before: snapshotTask(task),
      after: deleted ? null : { ...snapshotTask(task), ...fields }
    }));
    await updateTasks(taskActionUpdates(changes));
    if (action !== "dismiss-send") pushUndoEntry({ label: action, diffs });
    const deletedIds = new Set(changes.filter(change => change.deleted).map(change => change.task.id));
    for (const id of deletedIds) {
      saveCoordinator.forget(id);
      dirtySnapshots.delete(id);
      markLogicalDeleted(resolveLogicalId(id), id);
    }
    if (deletedIds.size) {
      const list = getOrderedTasksForPage(tasks[0]);
      const index = list.findIndex(task => task.id === tasks[0].id);
      const survivor = list.slice(index).find(task => !deletedIds.has(task.id))
        || list.slice(0, index).findLast(task => !deletedIds.has(task.id));
      focusTaskId.value = survivor ? { taskId: survivor.id, selection: { from: 1, to: 1 } } : null;
      newTasks.value = newTasks.value.filter(task => !deletedIds.has(task.id));
      mainTasks.value = mainTasks.value.filter(task => !deletedIds.has(task.id));
    }
    if (action === "recurrence" && value) {
      tasks.forEach(task => recurrenceCache.set(task.id, value));
      await runRecurrenceGeneration();
    }
    await loadDashboard();
    if (activeTab?.value === "History") await loadHistory?.();
    await nextTick();
  });

  const setTaskCategory = queueTaskAction(async (selection, category) => {
    if (!(await saveCoordinator.flushAll()).ok) throw new Error("Could not save edits before moving the tasks.");
    const tasks = resolveMoveTasks(Array.isArray(selection) ? selection : [selection]);
    if (incompleteTaskGroup(tasks, taskGroupRows)) throw new Error(detachBeforeMove);
    const categoryId = category === "this-week" ? `day-${getDayKey()}`
      : category === "next-week" ? `week-${formatDateKey(relativeWeekStart(1))}` : category;
    const selectedIds = new Set(tasks.map(task => task.id));
    // Append a group in visual order. A single Move keeps its existing position.
    const lastPosition = mainTasks.value.reduce((last, task) =>
      selectedIds.has(task.id) ? last : Math.max(last, task.position), 0);
    const updates = tasks.map((task, index) => {
      const change = buildCategoryUpdate(task, categoryId);
      if (!change) return null;
      return {
        baseUpdatedAt: task.updatedAt, page: DASHBOARD_MAIN_PAGE, ...change,
        ...(tasks.length > 1 ? { position: lastPosition + index + 1 } : {})
      };
    });
    if (updates.some(update => !update)) return;
    const entry = await persistTaskMoves(tasks, updates, tasks[0].id);
    if (entry) {
      const label = category.replaceAll('-', ' ');
      const message = tasks.length > 1 ? `Moved ${tasks.length} tasks to ${label}.` : `Moved to ${label}.`;
      announceAction(message, undo, () => undoStack.value.at(-1) === entry);
    }
  });

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
    return categoryId === "new"
      ? { scheduledDate: null, recurrence: null }
      : buildCategoryUpdate(task, categoryId);
  };

  const applyTaskMoves = queueTaskAction(async (selection, categoryId, positions, scheduledDateOverride = null) => {
    if (!(await saveCoordinator.flushAll()).ok) throw new Error("Could not save edits before moving the tasks.");
    const tasks = resolveMoveTasks(selection);
    const updates = tasks.map((task, index) => ({
      baseUpdatedAt: task.updatedAt,
      position: positions[index],
      page: categoryId === "new" ? DASHBOARD_NEW_PAGE : DASHBOARD_MAIN_PAGE,
      ...buildCategoryUpdateForMove(task, categoryId),
      ...(scheduledDateOverride ? { scheduledDate: scheduledDateOverride } : {})
    }));
    await persistTaskMoves(tasks, updates, tasks[0].id);
  });

  const applyTaskMove = (task, categoryId, position, scheduledDateOverride) =>
    applyTaskMoves([task], categoryId, [position], scheduledDateOverride);

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
      if (!(await saveCoordinator.flushAll()).ok) return;
      await archivePreviousCompletions();
      await runRecurrenceGeneration();
      await runDailyMaintenance();
      await loadDashboard();
      if (activeTab?.value === "History") {
        await loadHistory?.();
      }
      await loadWarnings?.();
      currentDayKey.value = nextDay; // failures retain the old key for the next focus/timer retry
    }
  };

  const initDayKey = () => {
    currentDayKey.value = getDayKey();
  };

  return {
    recordClipboard: (clipboard) => pushUndoEntry({ clipboard }),
    newTasks,
    mainTasks,
    maximizedCategoryId,
    focusTaskId,
    focusContentTarget,
    highlightTaskId,
    highlightNonce,
    mainCategories,
    loadDashboard,
    navigateToTask,
    createTask,
    createTaskBelow,
    undo,
    redo,
    saveTask,
    deleteTask,
    toggleComplete,
    moveNewToMain,
    setTaskCategory,
    changeTaskGroup,
    applyTaskAction,
    setTaskRecurrence,
    splitSubcontentToNewTask,
    splitTitleToNewTask,
    moveTaskToPrevious,
    navigateHorizontal,
    mergeTaskToPrevious,
    mergeTaskWithNext: task => mergeTaskToPrevious(task, true),
    focusPrevTaskFromTitle,
    focusNextTaskFromContent,
    handleDirtyChange,
    applyTaskMove,
    applyTaskMoves,
    findTaskById,
    getCategoryTasks,
    taskGroupRows,
    getOrderedTasksForPage,
    undoSignal,
    pollChanges,
    handleDayTick,
    initDayKey,
    currentDayKey
  };
};
