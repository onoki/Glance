import { horizontalTaskTarget } from "../utils/taskNavigation.js";
import { joinTaskDocuments } from "../utils/taskJoin.js";
import { saveCoordinator } from "../services/saveCoordinator.js";
import { ref } from "vue";
import {
  completeTask as completeTaskApi,
  createTask as createTaskApi,
  deleteTask as deleteTaskApi,
  restoreTask as restoreTaskApi,
  updateTask as updateTaskApi
} from "../api/tasks.js";
import { fetchPersonTasks as fetchPersonTasksApi } from "../api/people.js";
import { isDocEmptyJson } from "../utils/taskDocUtils.js";
import { shouldDeleteEmptyOnComplete } from "../utils/taskCompletionUtils.js";
import {
  emptyContentDoc,
  emptyTitleDoc,
  normalizeContent,
  normalizeTask,
  normalizeTitle,
  titleDocToListItem
} from "../utils/taskUtils.js";

const PEOPLE_PAGE = "people:main";

export const usePeopleTasks = ({ selectedPersonId, onHistoryChange, api = {}, history = { undo: [], redo: [] } }) => {
  const completeTask = api.completeTask || completeTaskApi;
  const createTask = api.createTask || createTaskApi;
  const deleteTask = api.deleteTask || deleteTaskApi;
  const restoreTask = api.restoreTask || restoreTaskApi;
  const updateTask = api.updateTask || updateTaskApi;
  const fetchPersonTasks = api.fetchPersonTasks || fetchPersonTasksApi;
  const tasks = ref([]);
  const focusTaskId = ref(null);
  const focusContentTarget = ref(null);
  const dragOver = ref({ id: null, position: "before" });
  const undoSignal = ref(0);
  const dirtySnapshots = new Map();
  const persistedTasks = new Map();
  const undoStack = history.undo;
  const redoStack = history.redo;
  const undoLimit = 100;
  let dragTaskId = null;
  let loadSequence = 0;
  let creatingBlankFor = null;

  const cloneTask = (task) => normalizeTask(JSON.parse(JSON.stringify(task)));

  const pushDeleteUndo = (task) => {
    undoStack.push({ type: "delete", task: cloneTask(task) });
    if (undoStack.length > undoLimit) undoStack.shift();
    redoStack.length = 0;
  };

  const sortTasks = () => {
    tasks.value = [...tasks.value].sort((left, right) => left.position - right.position);
  };

  const handleDirtyChange = (id, dirty, task) => {
    if (dirty && task) {
      dirtySnapshots.set(id, normalizeTask(task));
      return;
    }
    dirtySnapshots.delete(id);
  };

  const mergeDirtyTasks = (incoming) => incoming.map((task) => dirtySnapshots.get(task.id) || task);
  const resolveTask = (task) => dirtySnapshots.get(task.id) || task;

  const positionAfter = (task) => {
    const index = task ? tasks.value.findIndex((item) => item.id === task.id) : tasks.value.length - 1;
    const previous = index >= 0 ? tasks.value[index] : null;
    const next = index >= 0 ? tasks.value[index + 1] : null;
    if (previous && next) return (previous.position + next.position) / 2;
    if (previous) return previous.position + 1;
    return Date.now();
  };

  const createTaskBelow = async (sourceTask = null, ...args) => {
    const overrides = args[1] || null;
    const personId = selectedPersonId.value;
    if (!personId) return null;
    const payload = {
      page: PEOPLE_PAGE,
      title: normalizeTitle(overrides?.title ?? emptyTitleDoc()),
      content: normalizeContent(overrides?.content ?? emptyContentDoc()),
      position: positionAfter(sourceTask),
      ownerPersonId: personId
    };
    const response = await createTask(payload);
    if (selectedPersonId.value !== personId) return response.taskId;
    tasks.value.push(normalizeTask({
      ...payload,
      id: response.taskId,
      createdAt: Date.now(),
      updatedAt: response.updatedAt,
      completedAt: null
    }));
    sortTasks();
    persistedTasks.set(response.taskId, cloneTask(tasks.value.find((task) => task.id === response.taskId)));
    focusTaskId.value = overrides?.titleSelection
      ? { taskId: response.taskId, selection: overrides.titleSelection } : response.taskId;
    return response.taskId;
  };

  const ensureBlankTask = async (personId) => {
    if (!personId || selectedPersonId.value !== personId) return;
    if (tasks.value.some((task) => !task.completedAt)) return;
    if (creatingBlankFor === personId) return;
    creatingBlankFor = personId;
    try {
      await createTaskBelow();
    } finally {
      if (creatingBlankFor === personId) creatingBlankFor = null;
    }
  };

  let switchingPerson = false;
  const loadTasks = async (requestedPersonId) => {
    // Polling must not cancel a deliberate person switch that is still loading.
    if (switchingPerson && requestedPersonId === undefined) return;
    const personId = requestedPersonId ?? selectedPersonId.value;
    const selectionAtRequest = selectedPersonId.value;
    const sequence = ++loadSequence;
    switchingPerson = personId !== selectionAtRequest;
    try {
      if (!personId) { tasks.value = []; return; }
      const response = await fetchPersonTasks(personId);
      if (sequence !== loadSequence || selectedPersonId.value !== selectionAtRequest) return;
      // Commit label, selection and rows in the same Vue update. Keep the old view
      // intact while fetching, and remount the list rather than animating people.
      selectedPersonId.value = personId;
      focusTaskId.value = null;
      focusContentTarget.value = null;
      for (const task of response.tasks || []) persistedTasks.set(task.id, cloneTask(task));
      tasks.value = mergeDirtyTasks((response.tasks || []).map(normalizeTask));
      sortTasks();
      await ensureBlankTask(personId);
    } finally {
      if (sequence === loadSequence) switchingPerson = false;
    }
  };

  const saveTask = async (payload) => {
    const stored = persistedTasks.get(payload.id) || tasks.value.find((item) => item.id === payload.id);
    const before = stored ? cloneTask(stored) : null;
    const title = normalizeTitle(payload.title);
    const content = normalizeContent(payload.content);
    const response = await updateTask(payload.id, {
      baseUpdatedAt: payload.baseUpdatedAt,
      title,
      content,
      page: PEOPLE_PAGE
    });
    const task = tasks.value.find((item) => item.id === payload.id);
    if (task) Object.assign(task, { title, content, updatedAt: response.updatedAt });
    if (task) persistedTasks.set(task.id, cloneTask(task));
    if (before && !payload.suppressUndo && (JSON.stringify(before.title) !== JSON.stringify(title) || JSON.stringify(before.content) !== JSON.stringify(content))) {
      undoStack.push({ type: "edit", task: before, after: cloneTask({ ...before, title, content, updatedAt: response.updatedAt }) });
      if (undoStack.length > undoLimit) undoStack.shift();
      redoStack.length = 0;
    }
    dirtySnapshots.delete(payload.id);
    return response;
  };

  const removeTask = async (task, options = {}) => {
    const current = resolveTask(task);
    const index = tasks.value.findIndex((item) => item.id === task.id);
    const previous = index > 0 ? tasks.value[index - 1] : null;
    const next = tasks.value[index + 1];
    await deleteTask(task.id);
    saveCoordinator.forget(task.id);
    if (options.recordUndo !== false) pushDeleteUndo(current);
    dirtySnapshots.delete(task.id);
    tasks.value = tasks.value.filter((item) => item.id !== task.id);
    focusTaskId.value = options.direction === "forward" && next
      ? { taskId: next.id, selection: { from: 1, to: 1 } } : previous?.id || null;
    await ensureBlankTask(selectedPersonId.value);
    onHistoryChange?.();
  };

  const undo = async () => {
    const entry = undoStack.pop();
    if (!entry) return false;
    try {
      if (entry.clipboard) {
        selectedPersonId.value = entry.personId;
        await entry.clipboard.undo();
        await loadTasks();
        redoStack.push(entry);
        return true;
      }
      selectedPersonId.value = entry.task.ownerPersonId;
      if (entry.type === "merge") {
        // Restore the source first: a later failed save must never discard text.
        const restored = await restoreTask(entry.source.id);
        await updateTask(entry.source.id, {
          baseUpdatedAt: restored.updatedAt,
          title: entry.source.title, content: entry.source.content, page: PEOPLE_PAGE,
          position: entry.source.position
        });
        await applyEditHistory(entry.task, entry.after);
        redoStack.push(entry);
        return true;
      }
      if (entry.type === "edit") {
        await applyEditHistory(entry.task, entry.after);
        redoStack.push(entry);
        return true;
      }
      const restored = await restoreTask(entry.task.id);
      await updateTask(entry.task.id, {
        baseUpdatedAt: restored.updatedAt,
        title: normalizeTitle(entry.task.title),
        content: normalizeContent(entry.task.content),
        page: entry.task.page,
        position: entry.task.position,
        scheduledDate: entry.task.scheduledDate ?? null,
        recurrence: entry.task.recurrence ?? null
      });
      redoStack.push(entry);
      await loadTasks();
      focusTaskId.value = entry.task.id;
      onHistoryChange?.();
      return true;
    } catch (error) {
      undoStack.push(entry);
      throw error;
    }
  };

  const redo = async () => {
    const entry = redoStack.pop();
    if (!entry) return false;
    try {
      if (entry.clipboard) {
        selectedPersonId.value = entry.personId;
        await entry.clipboard.redo();
        await loadTasks();
        undoStack.push(entry);
        return true;
      }
      selectedPersonId.value = entry.task.ownerPersonId;
      if (entry.type === "merge") {
        const response = await fetchPersonTasks(entry.source.ownerPersonId);
        const source = response.tasks.find(task => task.id === entry.source.id);
        if (!source || JSON.stringify(normalizeTitle(source.title)) !== JSON.stringify(entry.source.title) ||
            JSON.stringify(normalizeContent(source.content)) !== JSON.stringify(entry.source.content)) {
          throw new Error("The source note changed elsewhere. Redo was stopped to preserve its text.");
        }
        await applyEditHistory(entry.after, entry.task);
        await deleteTask(entry.source.id);
    saveCoordinator.forget(entry.source.id);
        await loadTasks();
        undoStack.push(entry);
        return true;
      }
      if (entry.type === "edit") {
        await applyEditHistory(entry.after, entry.task);
        undoStack.push(entry);
        return true;
      }
      await deleteTask(entry.task.id);
    saveCoordinator.forget(entry.task.id);
      dirtySnapshots.delete(entry.task.id);
      undoStack.push(entry);
      await loadTasks();
      onHistoryChange?.();
      return true;
    } catch (error) {
      redoStack.push(entry);
      throw error;
    }
  };

  const applyEditHistory = async (snapshot, expected) => {
    const response = await fetchPersonTasks(snapshot.ownerPersonId);
    const current = response.tasks.find((task) => task.id === snapshot.id);
    if (!current) throw new Error("This note is no longer available to undo or redo.");
    if (JSON.stringify(normalizeTitle(current.title)) !== JSON.stringify(expected.title) ||
        JSON.stringify(normalizeContent(current.content)) !== JSON.stringify(expected.content)) {
      throw new Error("This note changed elsewhere. Undo or redo was stopped to preserve the newer text.");
    }
    await updateTask(snapshot.id, {
      baseUpdatedAt: current.updatedAt,
      title: normalizeTitle(snapshot.title), content: normalizeContent(snapshot.content), page: PEOPLE_PAGE
    });
    dirtySnapshots.delete(snapshot.id);
    await loadTasks();
    undoSignal.value += 1;
    focusTaskId.value = snapshot.id;
  };

  const toggleComplete = async (task) => {
    const current = resolveTask(task);
    if (shouldDeleteEmptyOnComplete(current)) {
      await removeTask(current);
      return;
    }
    if (dirtySnapshots.has(current.id)) {
      await saveTask({
        id: current.id,
        title: current.title,
        content: current.content,
        baseUpdatedAt: current.updatedAt
      });
    }
    const completed = !current.completedAt;
    const response = await completeTask(current.id, { completed });
    const stored = tasks.value.find((item) => item.id === current.id);
    if (stored) stored.completedAt = completed ? response.completedAt || Date.now() : null;
    await loadTasks();
    onHistoryChange?.();
  };

  const moveTaskToPrevious = async (task) => {
    const index = tasks.value.findIndex((item) => item.id === task.id);
    if (index <= 0) return false;
    const previous = cloneTask(resolveTask(tasks.value[index - 1]));
    const current = cloneTask(resolveTask(task));
    const previousContent = normalizeContent(previous.content);
    const currentContent = normalizeContent(current.content);
    const previousItems = previousContent.content?.[0]?.content || [];
    const currentItems = currentContent.content?.[0]?.content || [];
    const keptPrevious = previousItems.filter((item) => !isDocEmptyJson(item));
    const keptCurrent = currentItems.filter((item) => !isDocEmptyJson(item));
    const content = {
      type: "doc",
      content: [{
        type: "bulletList",
        content: [...keptPrevious, titleDocToListItem(current.title), ...keptCurrent]
      }]
    };
    await saveTask({
      id: previous.id,
      title: previous.title,
      content,
      baseUpdatedAt: previous.updatedAt,
      suppressUndo: true
    });
    await deleteTask(current.id);
    saveCoordinator.forget(current.id);
    undoStack.push({ type: "merge", task: previous, source: current, after: cloneTask(persistedTasks.get(previous.id)) });
    if (undoStack.length > undoLimit) undoStack.shift();
    redoStack.length = 0;
    dirtySnapshots.delete(current.id);
    tasks.value = tasks.value.filter((item) => item.id !== current.id);
    focusContentTarget.value = {
      taskId: previous.id,
      listIndex: keptPrevious.length,
      atEnd: true
    };
    if (current.completedAt) onHistoryChange?.();
    return true;
  };

  const navigateHorizontal = (task, direction, vertical = null) => {
    const list = tasks.value;
    const target = horizontalTaskTarget(list.map(resolveTask), task, direction);
    if (!target) return;
    if (vertical) target.vertical = { ...vertical, direction };
    focusTaskId.value = null;
    focusContentTarget.value = null;
    if (target.area === 'content') focusContentTarget.value = target;
    else focusTaskId.value = target;
  };

  const mergeTaskToPrevious = async (task, forward = false) => {
    if (!(await saveCoordinator.flushAll()).ok) return false;
    const index = tasks.value.findIndex((item) => item.id === task.id) + (forward ? 1 : 0);
    if (index <= 0 || index >= tasks.value.length) return false;
    const previous = cloneTask(resolveTask(tasks.value[index - 1]));
    const current = cloneTask(resolveTask(tasks.value[index]));
    const joined = joinTaskDocuments(previous, current);
    const mergedTitle = joined.title;
    const mergedContent = joined.content;
    await saveTask({
      id: previous.id,
      title: mergedTitle,
      content: mergedContent,
      baseUpdatedAt: previous.updatedAt,
      suppressUndo: true
    });
    await deleteTask(current.id);
    saveCoordinator.forget(current.id);
    undoStack.push({ type: "merge", task: previous, source: current, after: cloneTask(persistedTasks.get(previous.id)) });
    if (undoStack.length > undoLimit) undoStack.shift();
    redoStack.length = 0;
    dirtySnapshots.delete(current.id);
    tasks.value = tasks.value.filter((item) => item.id !== current.id);
    focusTaskId.value = null;
    focusContentTarget.value = null;
    if (joined.area === 'content') focusContentTarget.value = { taskId: previous.id, selection: joined.selection };
    else focusTaskId.value = { taskId: previous.id, selection: joined.selection };
    if (current.completedAt) onHistoryChange?.();
    return true;
  };

  const focusPreviousTask = (task, intent) => navigateHorizontal(task, -1, intent);

  const focusNextTask = (task, intent) => navigateHorizontal(task, 1, intent);

  const splitSubcontentToNewTask = (task, payload) => createTaskBelow(task, null, {
    title: payload?.title ?? emptyTitleDoc(),
    titleSelection: payload?.titleSelection,
    content: payload?.content ?? emptyContentDoc()
  });

  const splitTitleToNewTask = (task, _categoryId, payload) => createTaskBelow(task, null, {
    title: payload?.newTitle ?? emptyTitleDoc(),
    content: payload?.newContent ?? emptyContentDoc()
  });

  const startDrag = (task, event) => {
    dragTaskId = task.id;
    dragOver.value = { id: task.id, position: "before" };
    if (event?.dataTransfer) {
      event.dataTransfer.effectAllowed = "move";
      event.dataTransfer.setData("text/plain", task.id);
    }
  };

  const endDrag = () => {
    dragTaskId = null;
    dragOver.value = { id: null, position: "before" };
  };

  const setDragOver = (id, position) => {
    dragOver.value = {
      id,
      position: position || (dragOver.value.id === id ? dragOver.value.position : "before")
    };
  };

  const clearDragOver = (id) => {
    if (dragOver.value.id === id) dragOver.value = { id: null, position: "before" };
  };

  const dropOnTask = async (target, _categoryId, event) => {
    const id = dragTaskId || event?.dataTransfer?.getData("text/plain");
    const dragged = tasks.value.find((task) => task.id === id);
    if (!dragged || dragged.id === target.id) {
      endDrag();
      return;
    }
    const list = tasks.value.filter((task) => task.id !== dragged.id);
    const targetIndex = list.findIndex((task) => task.id === target.id);
    if (targetIndex < 0) {
      endDrag();
      return;
    }
    const after = dragOver.value.id === target.id && dragOver.value.position === "after";
    const previous = after ? list[targetIndex] : list[targetIndex - 1] || null;
    const next = after ? list[targetIndex + 1] || null : list[targetIndex];
    const position = previous && next
      ? (previous.position + next.position) / 2
      : previous
        ? previous.position + 1
        : next.position - 1;
    const response = await updateTask(dragged.id, {
      baseUpdatedAt: dragged.updatedAt,
      position
    });
    dragged.position = position;
    dragged.updatedAt = response.updatedAt;
    sortTasks();
    endDrag();
  };

  return {
    recordClipboard: (clipboard) => {
      undoStack.push({ clipboard, personId: selectedPersonId.value });
      if (undoStack.length > undoLimit) undoStack.shift();
      redoStack.length = 0;
    },
    tasks,
    focusTaskId,
    undoSignal,
    focusContentTarget,
    dragOver,
    loadTasks,
    undo,
    redo,
    saveTask,
    removeTask,
    toggleComplete,
    createTaskBelow,
    moveTaskToPrevious,
    navigateHorizontal,
    mergeTaskToPrevious,
    mergeTaskWithNext: task => mergeTaskToPrevious(task, true),
    focusPreviousTask,
    focusNextTask,
    splitSubcontentToNewTask,
    splitTitleToNewTask,
    handleDirtyChange,
    startDrag,
    endDrag,
    setDragOver,
    clearDragOver,
    dropOnTask
  };
};
