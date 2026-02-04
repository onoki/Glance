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
  mergeTitleDocs,
  normalizeContent,
  normalizeTask,
  normalizeTitle,
  titleDocToListItem
} from "../utils/taskUtils.js";
import { shouldDeleteEmptyOnComplete } from "../utils/taskCompletionUtils.js";
import { isDocEmptyJson } from "../utils/taskDocUtils.js";

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

  const undoStack = ref([]);
  const redoStack = ref([]);
  const undoLimit = 100;
  let isApplyingUndo = false;
  let suppressUndoCount = 0;
  const undoSignal = ref(0);
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
    logicalToActual.set(logicalId, null);
    if (actualId) {
      actualToLogical.delete(actualId);
    }
  };

  const snapshotTask = (task) => ({
    page: task.page,
    title: normalizeTitle(task.title),
    content: normalizeContent(task.content),
    position: task.position,
    scheduledDate: task.scheduledDate ?? null,
    recurrence: task.recurrence ?? null
  });

  const snapshotFromPayload = (payload) => ({
    page: payload.page,
    title: normalizeTitle(payload.title),
    content: normalizeContent(payload.content),
    position: payload.position,
    scheduledDate: payload.scheduledDate ?? null,
    recurrence: payload.recurrence ?? null
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

  const isTaskVisibleToday = (task, cutoff) => !task.completedAt || task.completedAt >= cutoff;

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
      completedAt: null,
      scheduledDate: payload.scheduledDate,
      recurrence: payload.recurrence
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
    const payload = {
      page,
      title: normalizeTitle(titleOverride ?? emptyTitleDoc()),
      content: normalizeContent(contentOverride || emptyContentDoc()),
      position: positionOverride || Date.now(),
      scheduledDate,
      recurrence
    };
    return createTaskInternal(payload, options);
  };

  const createTaskBelow = async (task, categoryId = null, overrides = null, options = {}) => {
    const list = task.page === DASHBOARD_NEW_PAGE ? newTasks.value : mainTasks.value;
    const index = list.findIndex((item) => item.id === task.id);
    const next = index >= 0 ? list[index + 1] : null;
    const position = next ? (task.position + next.position) / 2 : task.position + 1;
    const title = overrides?.title ?? emptyTitleDoc();
    const content = overrides?.content ?? emptyContentDoc();
    const newId = await createTask(task.page, title, content, position, categoryId, options);
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

  const saveTask = async ({ id, title, content, baseUpdatedAt, page, suppressUndo = false }) => {
    const stored = getStoredTaskById(id);
    const logicalId = stored ? resolveLogicalId(stored.id) : resolveLogicalId(id);
    const beforeSnapshot = stored ? snapshotTask(stored) : null;
    const normalizedTitle = normalizeTitle(title);
    const normalizedContent = normalizeContent(content);
    const afterSnapshot = beforeSnapshot
      ? { ...beforeSnapshot, title: normalizedTitle, content: normalizedContent }
      : {
          page: page ?? stored?.page ?? DASHBOARD_MAIN_PAGE,
          title: normalizedTitle,
          content: normalizedContent,
          position: stored?.position ?? Date.now(),
          scheduledDate: stored?.scheduledDate ?? null,
          recurrence: stored?.recurrence ?? null
        };

    if (!suppressUndo && beforeSnapshot && !snapshotsEqual(beforeSnapshot, afterSnapshot)) {
      pushUndoEntry({
        label: "edit",
        diffs: [{ logicalId, before: beforeSnapshot, after: afterSnapshot }]
      });
    }

    const response = await updateTaskApi(id, {
      baseUpdatedAt,
      title: normalizedTitle,
      content: normalizedContent,
      page
    });
    updateTaskLocal(id, { title: normalizedTitle, content: normalizedContent, updatedAt: response.updatedAt });
    await loadDashboard();
  };

  const deleteTask = async (task, options = {}) => {
    if (!task?.id) {
      return;
    }
    const snapshot = resolveTaskSnapshot(task);
    const logicalId = snapshot ? resolveLogicalId(snapshot.id) : resolveLogicalId(task.id);
    const beforeSnapshot = snapshot ? snapshotTask(snapshot) : null;
    const list = getOrderedTasksForPage(task);
    const index = list.findIndex((item) => item.id === task.id);
    const prevTask = index > 0 ? list[index - 1] : null;
    try {
      await deleteTaskApi(task.id);
    } catch {
      return;
    }
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
    const snapshots = newTasks.value.map((task) => resolveTaskSnapshot(task));
    const diffs = snapshots.map((task) => {
      const logicalId = resolveLogicalId(task.id);
      const before = snapshotTask(task);
      const after = { ...before, page: DASHBOARD_MAIN_PAGE };
      return { logicalId, before, after };
    });
    const updates = snapshots.map((task) =>
      updateTaskApi(task.id, {
        baseUpdatedAt: task.updatedAt,
        title: task.title,
        content: task.content,
        page: DASHBOARD_MAIN_PAGE
      })
    );
    await Promise.all(updates);
    await loadDashboard();
    if (diffs.length > 0) {
      pushUndoEntry({
        label: "move",
        diffs
      });
    }
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

    const previous = resolveTaskSnapshot(list[index - 1]);
    const current = resolveTaskSnapshot(task);
    const prevContent = normalizeContent(previous.content);
    const prevList = prevContent.content?.[0]?.content ?? [];
    const currentContent = normalizeContent(current.content);
    const currentList = currentContent.content?.[0]?.content ?? [];
    const titleItem = titleDocToListItem(current.title);
    const filteredPrevList = prevList.filter((item) => !isDocEmptyJson(item));
    const filteredCurrentList = currentList.filter((item) => !isDocEmptyJson(item));

    const merged = {
      type: "doc",
      content: [
        {
          type: "bulletList",
          content: [...filteredPrevList, titleItem, ...filteredCurrentList]
        }
      ]
    };

    const prevLogicalId = resolveLogicalId(previous.id);
    const currentLogicalId = resolveLogicalId(current.id);
    const beforePrevSnapshot = snapshotTask(previous);
    const beforeCurrentSnapshot = snapshotTask(current);
    const afterPrevSnapshot = { ...beforePrevSnapshot, content: merged };
    const afterCurrentSnapshot = {
      ...beforeCurrentSnapshot,
      page: "dashboard:hidden",
      content: currentContent,
      title: current.title || "Untitled"
    };

    await saveTask({
      id: previous.id,
      title: previous.title,
      content: merged,
      baseUpdatedAt: previous.updatedAt,
      page: previous.page,
      suppressUndo: true
    });

    await saveTask({
      id: current.id,
      title: current.title || "Untitled",
      content: currentContent,
      baseUpdatedAt: current.updatedAt,
      page: "dashboard:hidden",
      suppressUndo: true
    });

    focusContentTarget.value = {
      taskId: previous.id,
      listIndex: filteredPrevList.length,
      atEnd: true
    };

    newTasks.value = newTasks.value.filter((item) => item.id !== task.id);
    mainTasks.value = mainTasks.value.filter((item) => item.id !== task.id);

    pushUndoEntry({
      label: "indent",
      diffs: [
        { logicalId: prevLogicalId, before: beforePrevSnapshot, after: afterPrevSnapshot },
        { logicalId: currentLogicalId, before: beforeCurrentSnapshot, after: afterCurrentSnapshot }
      ]
    });

    return true;
  };

  const mergeTaskToPrevious = async (task) => {
    const list = task.page === DASHBOARD_NEW_PAGE ? newTasks.value : mainTasks.value;
    const index = list.findIndex((item) => item.id === task.id);
    if (index <= 0) {
      return false;
    }

    const previous = resolveTaskSnapshot(list[index - 1]);
    const current = resolveTaskSnapshot(task);
    if (!previous || !current) {
      return false;
    }

    const mergedTitle = mergeTitleDocs(previous.title, current.title);
    const prevContent = normalizeContent(previous.content);
    const currentContent = normalizeContent(current.content);
    const prevList = prevContent.content?.[0]?.content ?? [];
    const currentList = currentContent.content?.[0]?.content ?? [];
    const filteredPrevList = prevList.filter((item) => !isDocEmptyJson(item));
    const filteredCurrentList = currentList.filter((item) => !isDocEmptyJson(item));
    const mergedList = [...filteredPrevList, ...filteredCurrentList];
    const mergedContent = mergedList.length
      ? {
          type: "doc",
          content: [
            {
              type: "bulletList",
              content: mergedList
            }
          ]
        }
      : emptyContentDoc();

    const prevLogicalId = resolveLogicalId(previous.id);
    const currentLogicalId = resolveLogicalId(current.id);
    const beforePrevSnapshot = snapshotTask(previous);
    const beforeCurrentSnapshot = snapshotTask(current);
    const afterPrevSnapshot = { ...beforePrevSnapshot, title: mergedTitle, content: mergedContent };
    const afterCurrentSnapshot = {
      ...beforeCurrentSnapshot,
      page: "dashboard:hidden",
      title: emptyTitleDoc(),
      content: emptyContentDoc()
    };

    await saveTask({
      id: previous.id,
      title: mergedTitle,
      content: mergedContent,
      baseUpdatedAt: previous.updatedAt,
      page: previous.page,
      suppressUndo: true
    });

    await saveTask({
      id: current.id,
      title: emptyTitleDoc(),
      content: emptyContentDoc(),
      baseUpdatedAt: current.updatedAt,
      page: "dashboard:hidden",
      suppressUndo: true
    });

    dirtySnapshots.delete(previous.id);
    dirtySnapshots.delete(current.id);
    focusTaskId.value = previous.id;

    newTasks.value = newTasks.value.filter((item) => item.id !== current.id);
    mainTasks.value = mainTasks.value.filter((item) => item.id !== current.id);

    pushUndoEntry({
      label: "merge",
      diffs: [
        { logicalId: prevLogicalId, before: beforePrevSnapshot, after: afterPrevSnapshot },
        { logicalId: currentLogicalId, before: beforeCurrentSnapshot, after: afterCurrentSnapshot }
      ]
    });

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
    const snapshot = resolveTaskSnapshot(task);
    const logicalId = snapshot ? resolveLogicalId(snapshot.id) : resolveLogicalId(task.id);
    const beforeSnapshot = snapshot
      ? { ...snapshotTask(snapshot), content: payload?.previousContent ?? snapshot.content }
      : null;
    const afterSnapshot = beforeSnapshot && payload?.remainingContent
      ? { ...beforeSnapshot, content: payload.remainingContent }
      : null;
    const newId = await createTask(
      task.page,
      payload.title,
      payload.content,
      position,
      categoryId,
      { suppressUndo: true }
    );
    focusTaskId.value = newId;

    const newTask = getStoredTaskById(newId);
    const newLogicalId = newTask ? resolveLogicalId(newTask.id) : resolveLogicalId(newId);
    const newAfterSnapshot = newTask ? snapshotTask(newTask) : snapshotFromPayload({
      page: task.page,
      title: payload.title,
      content: payload.content,
      position,
      scheduledDate: null,
      recurrence: null
    });

    if (beforeSnapshot && afterSnapshot) {
      pushUndoEntry({
        label: "split",
        diffs: [
          { logicalId, before: beforeSnapshot, after: afterSnapshot },
          { logicalId: newLogicalId, before: null, after: newAfterSnapshot }
        ]
      });
    }
  };

  const splitTitleToNewTask = async (task, categoryId, payload) => {
    const list = task.page === DASHBOARD_NEW_PAGE ? newTasks.value : mainTasks.value;
    const index = list.findIndex((item) => item.id === task.id);
    const next = index >= 0 ? list[index + 1] : null;
    const position = next ? (task.position + next.position) / 2 : task.position + 1;

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
      { suppressUndo: true }
    );
    focusTaskId.value = newId;

    const newTask = getStoredTaskById(newId);
    const newLogicalId = newTask ? resolveLogicalId(newTask.id) : resolveLogicalId(newId);
    const newAfterSnapshot = newTask ? snapshotTask(newTask) : snapshotFromPayload({
      page: task.page,
      title: payload?.newTitle ?? emptyTitleDoc(),
      content: payload?.newContent ?? emptyContentDoc(),
      position,
      scheduledDate: null,
      recurrence: null
    });

    if (beforeSnapshot && afterSnapshot) {
      pushUndoEntry({
        label: "split",
        diffs: [
          { logicalId, before: beforeSnapshot, after: afterSnapshot },
          { logicalId: newLogicalId, before: null, after: newAfterSnapshot }
        ]
      });
    }
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

  const applyUndoEntry = async (entry, direction) => {
    if (!entry?.diffs?.length) {
      return false;
    }
    const targetKey = direction === "undo" ? "before" : "after";
    isApplyingUndo = true;
    try {
      await withUndoSuppressed(async () => {
        dirtySnapshots.clear();

        for (const diff of entry.diffs) {
          const target = diff[targetKey];
          if (target !== null) {
            continue;
          }
          const actualId = resolveActualId(diff.logicalId);
          if (!actualId) {
            continue;
          }
          try {
            await deleteTaskApi(actualId);
          } catch {
            // ignore delete failures during undo
          }
          markLogicalDeleted(diff.logicalId, actualId);
        }

        for (const diff of entry.diffs) {
          const target = diff[targetKey];
          if (!target) {
            continue;
          }
          const actualId = resolveActualId(diff.logicalId);
          const exists = actualId && getStoredTaskById(actualId);
          if (exists) {
            continue;
          }
          const payload = {
            page: target.page,
            title: normalizeTitle(target.title),
            content: normalizeContent(target.content),
            position: target.position ?? Date.now(),
            scheduledDate: target.scheduledDate ?? null,
            recurrence: target.recurrence ?? null
          };
          try {
            const response = await createTaskApi(payload);
            updateMappingForCreate(diff.logicalId, response.taskId);
          } catch {
            // ignore create failures during undo
          }
        }

        for (const diff of entry.diffs) {
          const target = diff[targetKey];
          if (!target) {
            continue;
          }
          const actualId = resolveActualId(diff.logicalId);
          const existing = actualId ? getStoredTaskById(actualId) : null;
          if (!existing) {
            continue;
          }
          try {
            await updateTaskApi(actualId, {
              baseUpdatedAt: existing.updatedAt,
              title: normalizeTitle(target.title),
              content: normalizeContent(target.content),
              page: target.page,
              position: target.position,
              scheduledDate: target.scheduledDate ?? null,
              recurrence: target.recurrence ?? null
            });
          } catch {
            // ignore update failures during undo
          }
        }

        await loadDashboard();
        undoSignal.value += 1;
      });
      return true;
    } finally {
      isApplyingUndo = false;
    }
  };

  const undo = async () => {
    if (undoStack.value.length === 0) {
      return false;
    }
    const entry = undoStack.value[undoStack.value.length - 1];
    undoStack.value = undoStack.value.slice(0, -1);
    const applied = await applyUndoEntry(entry, "undo");
    if (applied) {
      redoStack.value = [...redoStack.value, entry];
    }
    return applied;
  };

  const redo = async () => {
    if (redoStack.value.length === 0) {
      return false;
    }
    const entry = redoStack.value[redoStack.value.length - 1];
    redoStack.value = redoStack.value.slice(0, -1);
    const applied = await applyUndoEntry(entry, "redo");
    if (applied) {
      undoStack.value = [...undoStack.value, entry];
    }
    return applied;
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
    const snapshot = resolveTaskSnapshot(task);
    const logicalId = snapshot ? resolveLogicalId(snapshot.id) : resolveLogicalId(task.id);
    const beforeSnapshot = snapshot ? snapshotTask(snapshot) : null;
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

    if (beforeSnapshot) {
      const afterSnapshot = {
        ...beforeSnapshot,
        page: update.page ?? beforeSnapshot.page,
        scheduledDate: update.scheduledDate ?? beforeSnapshot.scheduledDate,
        recurrence: update.recurrence ?? beforeSnapshot.recurrence
      };
      if (!snapshotsEqual(beforeSnapshot, afterSnapshot)) {
        pushUndoEntry({
          label: "move",
          diffs: [{ logicalId, before: beforeSnapshot, after: afterSnapshot }]
        });
      }
    }
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
    const snapshot = resolveTaskSnapshot(task);
    const logicalId = snapshot ? resolveLogicalId(snapshot.id) : resolveLogicalId(task.id);
    const beforeSnapshot = snapshot ? snapshotTask(snapshot) : null;
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

    if (beforeSnapshot) {
      const afterSnapshot = {
        ...beforeSnapshot,
        position: update.position ?? beforeSnapshot.position,
        scheduledDate: update.scheduledDate ?? beforeSnapshot.scheduledDate,
        recurrence: update.recurrence ?? beforeSnapshot.recurrence,
        page: update.page ?? beforeSnapshot.page
      };
      if (!snapshotsEqual(beforeSnapshot, afterSnapshot)) {
        pushUndoEntry({
          label: "move",
          diffs: [{ logicalId, before: beforeSnapshot, after: afterSnapshot }]
        });
      }
    }
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
    undo,
    redo,
    saveTask,
    deleteTask,
    toggleComplete,
    moveNewToMain,
    setTaskCategory,
    setTaskRecurrence,
    splitSubcontentToNewTask,
    splitTitleToNewTask,
    moveTaskToPrevious,
    mergeTaskToPrevious,
    focusPrevTaskFromTitle,
    focusNextTaskFromContent,
    handleDirtyChange,
    applyTaskMove,
    findTaskById,
    getCategoryTasks,
    getOrderedTasksForPage,
    undoSignal,
    pollChanges,
    handleDayTick,
    initDayKey,
    currentDayKey
  };
};
