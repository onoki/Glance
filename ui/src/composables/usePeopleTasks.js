import { applyTaskHistory, taskRevision } from "../utils/taskHistory.js";
import { horizontalTaskTarget } from "../utils/taskNavigation.js";
import { indentTaskDocuments, joinTaskDocuments, createTaskActionQueue } from "../utils/taskJoin.js";
import { saveCoordinator } from "../services/saveCoordinator.js";
import { nextTick, ref } from "vue";
import {
  completeTask as completeTaskApi,
  createTask as createTaskApi,
  deleteTask as deleteTaskApi,
  restoreTask as restoreTaskApi,
  updateTask as updateTaskApi
} from "../api/tasks.js";
import { fetchPersonTasks as fetchPersonTasksApi } from "../api/people.js";
import { shouldDeleteEmptyOnComplete } from "../utils/taskCompletionUtils.js";
import {
  emptyContentDoc,
  emptyTitleDoc,
  normalizeContent,
  normalizeTask,
  normalizeTitle
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
  const queueTaskAction = createTaskActionQueue();
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

  const positionAfter = (task, before = false) => {
    const index = task ? tasks.value.findIndex((item) => item.id === task.id) : tasks.value.length - 1;
    const previous = index >= 0 ? tasks.value[index] : null;
    const next = index >= 0 ? tasks.value[index + (before ? -1 : 1)] : null;
    if (previous && next) return (previous.position + next.position) / 2;
    if (previous) return previous.position + (before ? -1 : 1);
    return Date.now();
  };

  const createTaskBelowInternal = async (sourceTask = null, ...args) => {
    const overrides = args[1] || null;
    const options = args[2] || {};
    if (!(await saveCoordinator.flushAll()).ok) return null;
    const personId = selectedPersonId.value;
    if (!personId) return null;
    const payload = {
      page: PEOPLE_PAGE,
      title: normalizeTitle(overrides?.title ?? emptyTitleDoc()),
      content: normalizeContent(overrides?.content ?? emptyContentDoc()),
      position: positionAfter(sourceTask, options.before),
      ownerPersonId: personId,
      isPrivate: !!sourceTask?.isPrivate
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
    if (options.before) {
      undoStack.push({ type: 'create', task: cloneTask(tasks.value.find(task => task.id === response.taskId)), sourceId: sourceTask.id });
      if (undoStack.length > undoLimit) undoStack.shift();
      redoStack.length = 0;
    }
    focusTaskId.value = options.before ? { taskId: sourceTask.id, selection: { from: 1, to: 1 } } : overrides?.titleSelection
      ? { taskId: response.taskId, selection: overrides.titleSelection } : response.taskId;
    return response.taskId;
  };
  const createTaskBelow = queueTaskAction(createTaskBelowInternal);

  const ensureBlankTask = async (personId) => {
    if (!personId || selectedPersonId.value !== personId) return;
    if (tasks.value.some((task) => !task.completedAt)) return;
    if (creatingBlankFor === personId) return;
    creatingBlankFor = personId;
    try {
      await createTaskBelowInternal();
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
    const isPrivate = payload.isPrivate ?? stored?.isPrivate ?? false;
    const response = await updateTask(payload.id, {
      baseUpdatedAt: payload.baseUpdatedAt,
      title,
      content,
      isPrivate,
      page: PEOPLE_PAGE
    });
    const task = tasks.value.find((item) => item.id === payload.id);
    if (task) Object.assign(task, { title, content, isPrivate, updatedAt: response.updatedAt });
    const dirtySnapshot = dirtySnapshots.get(payload.id);
    if (dirtySnapshot) dirtySnapshot.updatedAt = response.updatedAt;
    if (task) persistedTasks.set(task.id, cloneTask(task));
    if (before && !payload.suppressUndo && (JSON.stringify(before.title) !== JSON.stringify(title) || JSON.stringify(before.content) !== JSON.stringify(content) || !!before.isPrivate !== isPrivate)) {
      undoStack.push({ type: "edit", task: before, after: cloneTask({ ...before, title, content, isPrivate, updatedAt: response.updatedAt }) });
      if (undoStack.length > undoLimit) undoStack.shift();
      redoStack.length = 0;
    }
    dirtySnapshots.delete(payload.id);
    return response;
  };

  const removeTask = queueTaskAction(async (task, options = {}) => {
    if (!(await saveCoordinator.flushAll()).ok) throw new Error("Could not save edits before deleting the note.");
    const current = resolveTask(task);
    if (dirtySnapshots.has(task.id)) {
      const saved = await saveTask({ ...current, baseUpdatedAt: taskRevision(current), suppressUndo: true });
      current.updatedAt = saved.updatedAt;
    }
    const index = tasks.value.findIndex((item) => item.id === task.id);
    const previous = index > 0 ? tasks.value[index - 1] : null;
    const next = tasks.value[index + 1];
    await deleteTask(task.id, taskRevision(current));
    saveCoordinator.forget(task.id);
    if (options.recordUndo !== false) pushDeleteUndo(current);
    dirtySnapshots.delete(task.id);
    tasks.value = tasks.value.filter((item) => item.id !== task.id);
    focusTaskId.value = options.direction === "forward" && next
      ? { taskId: next.id, selection: { from: 1, to: 1 } } : previous?.id || null;
    await ensureBlankTask(selectedPersonId.value);
    onHistoryChange?.();
  });

  const historyDiffs = entry => {
    const diff = (before, after) => ({ id: (before || after).id, before, after });
    if (entry.type === 'merge') return [diff(entry.task, entry.after), diff(entry.source, null)];
    if (entry.type === 'split') return [diff(entry.source, entry.afterSource), diff(null, entry.task)];
    if (entry.type === 'edit') return [diff(entry.task, entry.after)];
    return entry.type === 'create' ? [diff(null, entry.task)] : [diff(entry.task, null)];
  };

  const applyHistory = async (entry, direction) => {
    if (!(await saveCoordinator.flushAll()).ok) return false;
    selectedPersonId.value = entry.clipboard ? entry.personId : entry.task.ownerPersonId;
    try {
      if (entry.clipboard) return await entry.clipboard[direction]();
      return await applyTaskHistory(historyDiffs(entry), direction, {
        fetchTasks: async () => (await fetchPersonTasks(selectedPersonId.value)).tasks.map(normalizeTask),
        resolveId: diff => diff.id, restoreTask, updateTask, deleteTask,
        forget: id => saveCoordinator.forget(id)
      });
    } finally {
      await loadTasks();
      undoSignal.value += 1;
      const focusId = entry.type === 'create' ? entry.sourceId
        : entry.type === 'split' && direction === 'undo' ? entry.source.id : entry.task?.id;
      focusTaskId.value = tasks.value.some(task => task.id === focusId)
        ? entry.type === 'create' ? { taskId: focusId, selection: { from: 1, to: 1 } } : focusId : null;
      onHistoryChange?.();
    }
  };

  const changeHistory = (from, to, direction) => queueTaskAction(async () => {
    const entry = from.pop();
    if (!entry) return false;
    try {
      const applied = await applyHistory(entry, direction);
      if (applied) to.push(entry);
      else from.push(entry);
      return applied;
    } catch (error) {
      from.push(entry);
      throw error;
    }
  });
  const undo = changeHistory(undoStack, redoStack, 'undo');
  const redo = changeHistory(redoStack, undoStack, 'redo');

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

  const moveTaskToPrevious = queueTaskAction(async (task) => {
    if (!(await saveCoordinator.flushAll()).ok) return null;
    const index = tasks.value.findIndex((item) => item.id === task.id);
    if (index <= 0) return false;
    const previous = cloneTask(resolveTask(tasks.value[index - 1]));
    const current = cloneTask(resolveTask(tasks.value[index]));
    if (dirtySnapshots.has(current.id)) {
      const saved = await saveTask({ ...current, baseUpdatedAt: current.updatedAt, suppressUndo: true });
      current.updatedAt = saved.updatedAt;
    }
    const { content, listIndex } = indentTaskDocuments(previous, current);
    await saveTask({
      id: previous.id,
      title: previous.title,
      content,
      isPrivate: !!(previous.isPrivate || current.isPrivate),
      baseUpdatedAt: previous.updatedAt,
      suppressUndo: true
    });
    undoStack.push({ type: "merge", task: previous, source: current, after: cloneTask(persistedTasks.get(previous.id)) });
    if (undoStack.length > undoLimit) undoStack.shift();
    redoStack.length = 0;
    await deleteTask(current.id, taskRevision(current));
    saveCoordinator.forget(current.id);
    dirtySnapshots.delete(current.id);
    tasks.value = tasks.value.filter((item) => item.id !== current.id);
    focusContentTarget.value = {
      taskId: previous.id,
      listIndex,
      atEnd: true
    };
    if (current.completedAt) onHistoryChange?.();
    return true;
  });

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

  const mergeTaskToPrevious = queueTaskAction(async (task, forward = false) => {
    if (!(await saveCoordinator.flushAll()).ok) return false;
    const index = tasks.value.findIndex((item) => item.id === task.id) + (forward ? 1 : 0);
    if (index <= 0 || index >= tasks.value.length) return false;
    const previous = cloneTask(resolveTask(tasks.value[index - 1]));
    const current = cloneTask(resolveTask(tasks.value[index]));
    if (dirtySnapshots.has(current.id)) {
      const saved = await saveTask({ ...current, baseUpdatedAt: taskRevision(current), suppressUndo: true });
      current.updatedAt = saved.updatedAt;
    }
    const joined = joinTaskDocuments(previous, current);
    const mergedTitle = joined.title;
    const mergedContent = joined.content;
    await saveTask({
      id: previous.id,
      title: mergedTitle,
      content: mergedContent,
      isPrivate: !!(previous.isPrivate || current.isPrivate),
      baseUpdatedAt: previous.updatedAt,
      suppressUndo: true
    });
    undoStack.push({ type: "merge", task: previous, source: current, after: cloneTask(persistedTasks.get(previous.id)) });
    if (undoStack.length > undoLimit) undoStack.shift();
    redoStack.length = 0;
    await deleteTask(current.id, taskRevision(current));
    saveCoordinator.forget(current.id);
    dirtySnapshots.delete(current.id);
    tasks.value = tasks.value.filter((item) => item.id !== current.id);
    focusTaskId.value = null;
    focusContentTarget.value = null;
    if (joined.area === 'content') focusContentTarget.value = { taskId: previous.id, selection: joined.selection };
    else focusTaskId.value = { taskId: previous.id, selection: joined.selection };
    if (current.completedAt) onHistoryChange?.();
    return true;
  });

  const focusPreviousTask = (task, intent) => navigateHorizontal(task, -1, intent);

  const focusNextTask = (task, intent) => navigateHorizontal(task, 1, intent);

  const splitSubcontentToNewTask = (task, payload) => splitTitleToNewTask(task, null, {
    newTitle: payload?.title, newContent: payload?.content,
    titleSelection: payload?.titleSelection, commitSource: payload?.commitSource
  });

  const splitTitleToNewTask = queueTaskAction(async (task, _categoryId, payload) => {
    if (!(await saveCoordinator.flushAll()).ok) return null;
    const source = cloneTask(resolveTask(tasks.value.find(row => row.id === task.id) || task));
    const newId = await createTaskBelowInternal(source, null, {
      title: payload?.newTitle ?? emptyTitleDoc(), content: payload?.newContent ?? emptyContentDoc(), titleSelection: payload?.titleSelection
    });
    if (!newId) return null;
    const created = cloneTask(persistedTasks.get(newId));
    try {
      if (payload?.commitSource && await payload.commitSource() === false) {
        throw new Error('The new note is saved, but the original could not be updated. Both versions have been kept.');
      }
    } catch (error) {
      undoStack.push({ type: 'create', task: created, sourceId: source.id });
      redoStack.length = 0;
      throw error;
    }
    if (payload?.commitSource) {
      undoStack.push({ type: 'split', task: created, source, afterSource: cloneTask(persistedTasks.get(source.id)) });
      if (undoStack.length > undoLimit) undoStack.shift();
      redoStack.length = 0;
    }
    return newId;
  });

  const startDrag = (task, event) => {
    dragTaskId = task.id;
    dragOver.value = { id: null, position: "before" };
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
    if (!dragTaskId || dragTaskId === id) {
      dragOver.value = { id: null, position: "before" };
      return;
    }
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
    const after = dragOver.value.id === target.id && dragOver.value.position === "after";
    if (!(await saveCoordinator.flushAll()).ok) { endDrag(); return; }
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
    persistedTasks.set(dragged.id, cloneTask(dragged));
    sortTasks();
    endDrag();
    // Let the editor acknowledge the metadata revision before another edit.
    await nextTick();
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
