<template>
  <div
    class="task-item"
    :class="{ completed: !!task.completedAt }"
    :draggable="false"
    @dragstart="handleDragStart"
    @dragover.prevent="handleDragOver"
    @dragenter.prevent="handleDragEnter"
    @dragleave="handleDragLeave"
    @drop.prevent="handleDrop"
    @dragend="handleDragEnd"
  >
    <div
      v-if="isDropTarget"
      class="drop-indicator"
      :class="{ after: dropPosition === 'after' }"
    ></div>
    <label class="task-check">
      <input type="checkbox" :checked="!!task.completedAt" :disabled="!allowToggle" @change="toggleComplete" />
      <span></span>
    </label>
    <div class="task-body" :class="{ 'with-actions': showCategoryActions }">
      <div v-if="showCategoryActions" class="task-actions" @click.stop>
        <button type="button" class="task-action" @click="setCategory('uncategorized')">Uncategorized</button>
        <button type="button" class="task-action" @click="setCategory('this-week')">This week</button>
        <button type="button" class="task-action" @click="setCategory('next-week')">Next week</button>
        <button type="button" class="task-action" @click="setCategory('no-date')">No date</button>
        <button type="button" class="task-action" @click="setCategory('repeatable')">Repeatable</button>
        <button type="button" class="task-action" @click="setCategory('notes')">Notes</button>
      </div>
      <div class="title-row">
        <button
          v-if="draggable && !readOnly"
          type="button"
          class="drag-handle"
          aria-label="Drag task"
          :draggable="true"
          @dragstart.stop="handleDragStart"
        >
          ::
        </button>
        <RichTextEditor
          ref="titleEditorRef"
          class="title-editor"
          v-model="title"
          mode="title"
          :editable="!readOnly"
          :on-dirty="handleTitleDirty"
          :on-split-to-new-task="noopSplit"
          :on-key-down="handleTitleKeydown"
        />
        <span v-if="task.scheduledDate && task.scheduledDate !== 'no-date'" class="task-date">
          {{ task.scheduledDate }}
        </span>
        <span v-if="dirty && !readOnly" class="dirty-badge">dirty</span>
      </div>
      <div v-if="showRecurrenceControls && !readOnly" class="recurrence-row">
        <label class="recurrence-label">Frequency</label>
        <select v-model="recurrenceType" class="recurrence-select" @change="applyRecurrence">
          <option value="weekly">Weekly</option>
          <option value="monthly">Monthly</option>
        </select>
        <div v-if="recurrenceType === 'weekly'" class="weekday-row">
          <button
            v-for="day in weekdayOptions"
            :key="day.value"
            type="button"
            class="weekday-chip"
            :class="{ active: weeklyDays.includes(day.value) }"
            @click="toggleWeekday(day.value)"
          >
            {{ day.label }}
          </button>
        </div>
        <div v-if="recurrenceType === 'monthly'" class="monthday-row">
          <label class="recurrence-label">Days</label>
          <input
            v-model="monthDaysInput"
            class="monthday-input"
            type="text"
            placeholder="1, 15, 30"
            @blur="applyRecurrence"
          />
        </div>
      </div>
      <RichTextEditor
        ref="contentEditorRef"
        v-model="content"
        :editable="!readOnly"
        :on-dirty="handleContentDirty"
        :on-split-to-new-task="handleSplitToNewTask"
        :on-key-down="handleContentKeydown"
      />
    </div>
  </div>
</template>

<script setup>
import { nextTick, ref, watch } from "vue";
import RichTextEditor from "./RichTextEditor.vue";

const props = defineProps({
  task: {
    type: Object,
    required: true
  },
  readOnly: {
    type: Boolean,
    default: false
  },
  allowToggle: {
    type: Boolean,
    default: true
  },
  showCategoryActions: {
    type: Boolean,
    default: false
  },
  showRecurrenceControls: {
    type: Boolean,
    default: false
  },
  draggable: {
    type: Boolean,
    default: false
  },
  isDropTarget: {
    type: Boolean,
    default: false
  },
  dropPosition: {
    type: String,
    default: "before"
  },
  dragCategoryId: {
    type: String,
    default: null
  },
  onSetCategory: {
    type: Function,
    default: null
  },
  onSetRecurrence: {
    type: Function,
    default: null
  },
  focusTitleId: {
    type: String,
    default: null
  },
  focusContentTarget: {
    type: Object,
    default: null
  },
  onSave: {
    type: Function,
    required: true
  },
  onComplete: {
    type: Function,
    required: true
  },
  onDirty: {
    type: Function,
    required: true
  },
  onCreateBelow: {
    type: Function,
    required: true
  },
  onTabToPrevious: {
    type: Function,
    required: true
  },
  onSplitToNewTask: {
    type: Function,
    required: true
  },
  onFocusPrevTaskFromTitle: {
    type: Function,
    required: true
  },
  onFocusNextTaskFromContent: {
    type: Function,
    required: true
  },
  onDragStart: {
    type: Function,
    default: null
  },
  onDragEnd: {
    type: Function,
    default: null
  },
  onDrop: {
    type: Function,
    default: null
  },
  onDragOver: {
    type: Function,
    default: null
  },
  onDragLeave: {
    type: Function,
    default: null
  }
});

const title = ref(props.task.title);
const content = ref(props.task.content);
const dirty = ref(false);
const titleEditorRef = ref(null);
const contentEditorRef = ref(null);
const recurrenceType = ref("");
const weeklyDays = ref([]);
const monthDaysInput = ref("");
let saveTimer = null;
let pendingCreateTimer = null;

const weekdayOptions = [
  { value: 1, label: "Mon" },
  { value: 2, label: "Tue" },
  { value: 3, label: "Wed" },
  { value: 4, label: "Thu" },
  { value: 5, label: "Fri" },
  { value: 6, label: "Sat" },
  { value: 7, label: "Sun" }
];

const snapshot = () => ({
  ...props.task,
  title: title.value,
  content: content.value
});

const markDirty = () => {
  if (!dirty.value) {
    dirty.value = true;
  }
  props.onDirty(props.task.id, true, snapshot());
};

const scheduleSave = () => {
  if (props.readOnly) {
    return;
  }
  markDirty();
  if (saveTimer) {
    clearTimeout(saveTimer);
  }
  saveTimer = setTimeout(saveNow, 800);
};

const saveNow = async () => {
  if (props.readOnly) {
    return;
  }
  if (!dirty.value) {
    return;
  }
  if (saveTimer) {
    clearTimeout(saveTimer);
    saveTimer = null;
  }
  if (!title.value || !title.value.content || title.value.content.length === 0) {
    title.value = {
      type: "doc",
      content: [{ type: "paragraph" }]
    };
  }
  try {
    await props.onSave({
      id: props.task.id,
      title: title.value,
      content: content.value,
      baseUpdatedAt: props.task.updatedAt,
      page: props.task.page
    });
    dirty.value = false;
    props.onDirty(props.task.id, false, null);
  } catch {
    // keep dirty so user can retry
  }
};

const handleTitleDirty = () => {
  if (props.readOnly) {
    return;
  }
  scheduleSave();
};

const handleContentDirty = () => {
  if (props.readOnly) {
    return;
  }
  scheduleSave();
};

const isSelectionAtEnd = (editor) => {
  if (!editor) {
    return false;
  }
  const { selection } = editor.state;
  if (!selection.empty) {
    return false;
  }
  return selection.$from.pos === selection.$from.end();
};

const isSelectionAtStart = (editor) => {
  if (!editor) {
    return false;
  }
  const { selection } = editor.state;
  if (!selection.empty) {
    return false;
  }
  return selection.$from.pos === selection.$from.start();
};

const handleTitleKeydown = (event, editor) => {
  if (props.readOnly) {
    return false;
  }
  if (event.key === "Tab") {
    if (pendingCreateTimer) {
      clearTimeout(pendingCreateTimer);
      pendingCreateTimer = null;
    }
    saveNow();
    props.onTabToPrevious(props.task).then((moved) => {
      if (!moved) {
        contentEditorRef.value?.insertParagraphIfEmpty();
      }
    });
    return true;
  }

  if (event.key === "ArrowDown") {
    if (!isSelectionAtEnd(editor)) {
      return false;
    }
    event.preventDefault();
    contentEditorRef.value?.focusListItem(0, "start");
    return true;
  }

  if (event.key === "ArrowUp") {
    if (!isSelectionAtStart(editor)) {
      return false;
    }
    event.preventDefault();
    props.onFocusPrevTaskFromTitle(props.task);
    return true;
  }

  if (event.key === "Enter" && !event.shiftKey) {
    if (pendingCreateTimer) {
      clearTimeout(pendingCreateTimer);
    }
    saveNow();
    pendingCreateTimer = setTimeout(() => {
      pendingCreateTimer = null;
      props.onCreateBelow(props.task);
    }, 250);
    return true;
  }

  return false;
};

const getListContext = (editor) => {
  if (!editor) {
    return null;
  }
  const { $from, empty } = editor.state.selection;
  if (!empty) {
    return null;
  }
  let listItemDepth = null;
  for (let depth = $from.depth; depth > 0; depth -= 1) {
    if ($from.node(depth).type.name === "listItem") {
      listItemDepth = depth;
      break;
    }
  }
  if (!listItemDepth) {
    return null;
  }
  const listDepth = listItemDepth - 1;
  const listNode = $from.node(listDepth);
  if (!listNode || listNode.type.name !== "bulletList" || listDepth !== 1) {
    return null;
  }
  const listIndex = $from.index(listDepth);
  const listCount = listNode.childCount;
  const atStart = $from.parentOffset === 0;
  const atEnd = $from.parentOffset === $from.parent.content.size;
  return { listIndex, listCount, atStart, atEnd };
};

const handleContentKeydown = (event, editor) => {
  if (props.readOnly) {
    return false;
  }
  if (event.key === "ArrowDown") {
    const ctx = getListContext(editor);
    if (ctx && ctx.listIndex === ctx.listCount - 1 && ctx.atEnd) {
      event.preventDefault();
      props.onFocusNextTaskFromContent(props.task);
      return true;
    }
  }
  if (event.key === "ArrowUp") {
    const ctx = getListContext(editor);
    if (ctx && ctx.listIndex === 0 && ctx.atStart) {
      event.preventDefault();
      titleEditorRef.value?.focus();
      return true;
    }
  }
  return false;
};

const toggleComplete = () => {
  if (!props.allowToggle) {
    return;
  }
  props.onComplete(props.task);
};

const isEditorTarget = (event) =>
  !!event?.target?.closest?.(".ProseMirror, .editor-surface");

const isHandleTarget = (event) => !!event?.target?.closest?.(".drag-handle");

const handleDragStart = (event) => {
  if (!props.draggable || !props.onDragStart) {
    return;
  }
  if (!isHandleTarget(event) || isEditorTarget(event)) {
    event.preventDefault();
    return;
  }
  props.onDragStart(props.task, event);
};

const handleDragOver = (event) => {
  if (!event) {
    return;
  }
  const rect = event.currentTarget?.getBoundingClientRect();
  if (!rect) {
    return;
  }
  const midpoint = rect.top + rect.height / 2;
  const position = event.clientY >= midpoint ? "after" : "before";
  props.onDragOver?.(props.task.id, position);
};

const handleDragEnter = () => {
  props.onDragOver?.(props.task.id);
};

const handleDragLeave = () => {
  props.onDragLeave?.(props.task.id);
};

const handleDrop = (event) => {
  if (!props.onDrop) {
    return;
  }
  event.stopPropagation();
  props.onDrop(props.task, props.dragCategoryId, event);
};

const handleDragEnd = () => {
  props.onDragEnd?.();
};

const handleSplitToNewTask = async (payload) => {
  if (props.readOnly) {
    return;
  }
  if (payload?.remainingContent) {
    content.value = payload.remainingContent;
    markDirty();
  }
  await saveNow();
  props.onSplitToNewTask(props.task, {
    title: payload?.title,
    content: payload?.content
  });
};

const noopSplit = () => {};

const setCategory = (category) => {
  if (props.readOnly || !props.onSetCategory) {
    return;
  }
  props.onSetCategory(props.task, category);
};

watch(
  () => props.task?.recurrence,
  (recurrence) => {
    if (!recurrence) {
      recurrenceType.value = "";
      weeklyDays.value = [];
      monthDaysInput.value = "";
      return;
    }
    if (recurrence.type === "repeatable") {
      recurrenceType.value = "weekly";
      weeklyDays.value = [];
      monthDaysInput.value = "";
    } else if (recurrence.type === "weekly") {
      const weekdays = Array.isArray(recurrence.weekdays) ? recurrence.weekdays : [];
      weeklyDays.value = weekdays.slice().sort((a, b) => a - b);
      recurrenceType.value = "weekly";
    } else if (recurrence.type === "monthly") {
      const monthDays = Array.isArray(recurrence.monthDays) ? recurrence.monthDays : [];
      monthDaysInput.value = monthDays.join(", ");
      recurrenceType.value = "monthly";
    } else {
      recurrenceType.value = "";
    }
  },
  { immediate: true }
);

const parseMonthDays = (value) => {
  const parts = value
    .split(",")
    .map((item) => item.trim())
    .filter((item) => item.length > 0)
    .map((item) => Number.parseInt(item, 10))
    .filter((item) => Number.isInteger(item) && item >= 1 && item <= 31);
  return Array.from(new Set(parts));
};

const buildRecurrencePayload = () => {
  if (!recurrenceType.value) {
    return null;
  }
  if (recurrenceType.value === "weekly") {
    return { type: "weekly", weekdays: weeklyDays.value.slice() };
  }
  if (recurrenceType.value === "monthly") {
    return { type: "monthly", monthDays: parseMonthDays(monthDaysInput.value) };
  }
  return null;
};

const applyRecurrence = () => {
  if (props.readOnly || !props.onSetRecurrence) {
    return;
  }
  props.onSetRecurrence(props.task, buildRecurrencePayload());
};

const toggleWeekday = (day) => {
  if (weeklyDays.value.includes(day)) {
    weeklyDays.value = weeklyDays.value.filter((value) => value !== day);
  } else {
    weeklyDays.value = [...weeklyDays.value, day].sort((a, b) => a - b);
  }
  applyRecurrence();
};

watch(
  () => props.focusTitleId,
  async (id) => {
    if (!id || id !== props.task.id) {
      return;
    }
    await nextTick();
    titleEditorRef.value?.focus();
  }
);

watch(
  () => props.focusContentTarget,
  async (target) => {
    if (!target || target.taskId !== props.task.id) {
      return;
    }
    await nextTick();
    contentEditorRef.value?.focusListItem(target.listIndex, target.atEnd ? "end" : "start");
  }
);

watch(
  () => props.task,
  (task) => {
    if (!task || dirty.value) {
      return;
    }
    title.value = task.title;
    content.value = task.content;
  },
  { deep: true }
);
</script>

<style scoped>
.task-item {
  display: grid;
  grid-template-columns: 28px 1fr;
  gap: 8px;
  padding: 4px 0;
  border: none;
  background: transparent;
  align-items: start;
  position: relative;
}

.drop-indicator {
  position: absolute;
  top: 0;
  left: 28px;
  right: 0;
  height: 2px;
  background: #d94a3d;
  border-radius: 2px;
  pointer-events: none;
  z-index: 3;
  transform: translateY(-100%);
}

.drop-indicator.after {
  top: 100%;
  transform: translateY(0);
}

.task-item.completed {
  color: #8a8177;
  text-decoration: line-through;
}

.task-check {
  display: grid;
  place-items: center;
  align-self: start;
  padding-top: 2px;
}

.task-check input {
  display: none;
}

.task-check span {
  width: 18px;
  height: 18px;
  border: 2px solid #6f665f;
  border-radius: 6px;
  display: inline-block;
  position: relative;
}

.task-check input:checked + span::after {
  content: "";
  position: absolute;
  inset: 3px;
  background: #6f665f;
  border-radius: 3px;
}

.task-body {
  display: flex;
  flex-direction: column;
  gap: 2px;
  position: relative;
}

.task-body.with-actions {
  padding-right: 140px;
}

.task-actions {
  position: absolute;
  top: -2px;
  right: 0;
  display: flex;
  gap: 6px;
  opacity: 0;
  pointer-events: none;
  flex-wrap: wrap;
  justify-content: flex-end;
  z-index: 2;
}

.task-item:hover .task-actions {
  opacity: 1;
  pointer-events: auto;
}

.task-action {
  border: 1px solid #d8c7b3;
  background: #fffaf3;
  color: #3a3129;
  padding: 2px 6px;
  border-radius: 999px;
  font-size: 0.7rem;
  cursor: pointer;
}

.recurrence-row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  align-items: center;
  font-size: 0.75rem;
  color: #6f665f;
}

.recurrence-label {
  font-weight: 600;
}

.recurrence-select {
  border: 1px solid #d8c7b3;
  border-radius: 10px;
  padding: 2px 8px;
  background: #fffaf3;
  font-size: 0.75rem;
}

.weekday-row {
  display: flex;
  gap: 4px;
  flex-wrap: wrap;
}

.weekday-chip {
  border: 1px solid #d8c7b3;
  background: #fffaf3;
  color: #3a3129;
  padding: 2px 6px;
  border-radius: 999px;
  font-size: 0.7rem;
  cursor: pointer;
}

.weekday-chip.active {
  background: #1f1b16;
  color: #f9f4ee;
  border-color: #1f1b16;
}

.monthday-row {
  display: flex;
  gap: 6px;
  align-items: center;
}

.monthday-input {
  border: 1px solid #d8c7b3;
  border-radius: 10px;
  padding: 2px 8px;
  background: #fffaf3;
  font-size: 0.75rem;
  width: 120px;
}

.title-row {
  display: flex;
  align-items: center;
  gap: 6px;
  padding-right: 0;
}

.title-editor {
  flex: 1;
}

.drag-handle {
  border: 1px solid #d8c7b3;
  background: #fffaf3;
  color: #6f665f;
  border-radius: 6px;
  padding: 0 6px;
  font-size: 0.8rem;
  line-height: 1.2rem;
  cursor: grab;
  user-select: none;
}

.drag-handle:active {
  cursor: grabbing;
}

.title-editor :deep(.editor-surface) {
  padding: 0;
}

.title-editor :deep(.ProseMirror) {
  font-weight: 600;
  font-size: 1rem;
  min-height: 1.2em;
}

.dirty-badge {
  font-size: 0.7rem;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  padding: 2px 6px;
  border-radius: 999px;
  background: #f6d7a7;
  color: #5a3f1b;
}

.task-date {
  font-size: 0.7rem;
  color: #6f665f;
  background: #fffaf3;
  border: 1px solid #e0d2c1;
  border-radius: 999px;
  padding: 2px 6px;
}
</style>
