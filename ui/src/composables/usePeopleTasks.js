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
  mergeTitleDocs,
  normalizeContent,
  normalizeTask,
  normalizeTitle,
  titleDocToListItem
} from "../utils/taskUtils.js";

const PEOPLE_PAGE = "people:main";

export const usePeopleTasks = ({ selectedPersonId, onHistoryChange, api = {} }) => {
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
  const dirtySnapshots = new Map();
  const undoStack = [];
  const redoStack = [];
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
    focusTaskId.value = response.taskId;
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

  const loadTasks = async () => {
    const personId = selectedPersonId.value;
    const sequence = ++loadSequence;
    focusTaskId.value = null;
    focusContentTarget.value = null;
    if (!personId) {
      tasks.value = [];
      return;
    }
    const response = await fetchPersonTasks(personId);
    if (sequence !== loadSequence || selectedPersonId.value !== personId) return;
    tasks.value = mergeDirtyTasks((response.tasks || []).map(normalizeTask));
    sortTasks();
    await ensureBlankTask(personId);
  };

  const saveTask = async (payload) => {
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
    dirtySnapshots.delete(payload.id);
    return response;
  };

  const removeTask = async (task, options = {}) => {
    const current = resolveTask(task);
    const index = tasks.value.findIndex((item) => item.id === task.id);
    const previous = index > 0 ? tasks.value[index - 1] : null;
    await deleteTask(task.id);
    if (options.recordUndo !== false) pushDeleteUndo(current);
    dirtySnapshots.delete(task.id);
    tasks.value = tasks.value.filter((item) => item.id !== task.id);
    focusTaskId.value = previous?.id || null;
    await ensureBlankTask(selectedPersonId.value);
    onHistoryChange?.();
  };

  const undo = async () => {
    const entry = undoStack.pop();
    if (!entry) return false;
    try {
      selectedPersonId.value = entry.task.ownerPersonId;
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
      selectedPersonId.value = entry.task.ownerPersonId;
      await deleteTask(entry.task.id);
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
    const previous = resolveTask(tasks.value[index - 1]);
    const current = resolveTask(task);
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
      baseUpdatedAt: previous.updatedAt
    });
    await deleteTask(current.id);
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

  const mergeTaskToPrevious = async (task) => {
    const index = tasks.value.findIndex((item) => item.id === task.id);
    if (index <= 0) return false;
    const previous = resolveTask(tasks.value[index - 1]);
    const current = resolveTask(task);
    const previousItems = normalizeContent(previous.content).content?.[0]?.content || [];
    const currentItems = normalizeContent(current.content).content?.[0]?.content || [];
    const mergedItems = [...previousItems, ...currentItems].filter((item) => !isDocEmptyJson(item));
    const content = mergedItems.length
      ? { type: "doc", content: [{ type: "bulletList", content: mergedItems }] }
      : emptyContentDoc();
    await saveTask({
      id: previous.id,
      title: mergeTitleDocs(previous.title, current.title),
      content,
      baseUpdatedAt: previous.updatedAt
    });
    await deleteTask(current.id);
    dirtySnapshots.delete(current.id);
    tasks.value = tasks.value.filter((item) => item.id !== current.id);
    focusTaskId.value = previous.id;
    if (current.completedAt) onHistoryChange?.();
    return true;
  };

  const focusPreviousTask = (task) => {
    const index = tasks.value.findIndex((item) => item.id === task.id);
    if (index <= 0) return;
    const previous = tasks.value[index - 1];
    const items = normalizeContent(previous.content).content?.[0]?.content || [];
    if (!items.length) {
      focusTaskId.value = previous.id;
      return;
    }
    focusContentTarget.value = { taskId: previous.id, listIndex: items.length - 1, atEnd: true };
  };

  const focusNextTask = async (task) => {
    const index = tasks.value.findIndex((item) => item.id === task.id);
    const next = index >= 0 ? tasks.value[index + 1] : null;
    if (next) {
      focusTaskId.value = next.id;
      return;
    }
    await createTaskBelow(task);
  };

  const splitSubcontentToNewTask = (task, payload) => createTaskBelow(task, null, {
    title: payload?.title ?? emptyTitleDoc(),
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
    tasks,
    focusTaskId,
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
    mergeTaskToPrevious,
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
