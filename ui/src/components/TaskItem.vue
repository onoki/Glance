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
      <TaskItemActions
        :show-category-actions="showCategoryActions"
        :draggable="draggable"
        :read-only="readOnly"
        @drag-start="handleDragStart"
        @set-category="setCategory"
      />
      <div class="title-row">
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
        <span v-if="dirty && !readOnly" class="dirty-indicator" aria-label="Unsaved changes"></span>
      </div>
      <RecurrenceControls
        v-if="showRecurrenceControls && !readOnly"
        v-model:recurrenceType="recurrenceType"
        v-model:monthDaysInput="monthDaysInput"
        :weekly-days="weeklyDays"
        :weekday-options="weekdayOptions"
        @apply="applyRecurrence"
        @toggle-weekday="toggleWeekday"
      />
      <RichTextEditor
        v-if="hasSubcontent"
        ref="contentEditorRef"
        class="content-editor"
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
import { computed, nextTick, onBeforeUnmount, ref, watch } from "vue";
import RichTextEditor from "./RichTextEditor.vue";
import RecurrenceControls from "./RecurrenceControls.vue";
import TaskItemActions from "./TaskItemActions.vue";
import { useTaskEditing } from "../composables/useTaskEditing.js";

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
  categoryId: {
    type: String,
    default: null
  },
  isLastInCategory: {
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
  onDelete: {
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

onBeforeUnmount(() => {
  if (saveTimer) {
    clearTimeout(saveTimer);
    saveTimer = null;
  }
});

const hasSubcontent = computed(() => {
  const doc = content.value;
  if (!doc || !doc.content || doc.content.length === 0) {
    return false;
  }
  const first = doc.content[0];
  return first?.type === "bulletList" && Array.isArray(first.content) && first.content.length > 0;
});

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

const { handleTitleKeydown, handleContentKeydown } = useTaskEditing({
  props,
  titleRef: title,
  contentRef: content,
  titleEditorRef,
  contentEditorRef,
  hasSubcontent,
  saveNow
});

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
  grid-template-columns: 20px 1fr;
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
  left: 20px;
  right: 0;
  height: 2px;
  background: #d94a3d;
  border-radius: 0;
  pointer-events: none;
  z-index: 3;
  transform: translateY(-100%);
}

.drop-indicator.after {
  top: 100%;
  transform: translateY(0);
}

.task-item.completed {
  color: var(--text-muted);
  text-decoration: line-through;
}

.task-check {
  display: grid;
  place-items: center;
  align-self: flex-start;
  padding-top: 1px;
}

.task-check input {
  display: none;
}

.task-check span {
  width: 12px;
  height: 12px;
  border: 1px solid var(--border-panel);
  border-radius: 0;
  display: inline-block;
  position: relative;
}

.task-check input:checked + span::after {
  content: "";
  position: absolute;
  inset: 2px;
  background: var(--text-main);
  border-radius: 0;
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

.title-row {
  display: flex;
  align-items: center;
  gap: 6px;
  padding-right: 0;
  position: relative;
}

.title-editor {
  flex: 1;
}

.title-editor :deep(.editor-surface) {
  padding: 0;
}

.title-editor :deep(.ProseMirror) {
  font-weight: 400;
  font-size: 1rem;
  color: var(--text-title);
  min-height: 1.2em;
}

.dirty-indicator {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--text-warning);
  margin-left: 4px;
}

.task-date {
  font-size: 0.7rem;
  color: var(--text-muted);
  background: var(--bg-panel);
  border: 1px solid var(--border-panel);
  border-radius: 0;
  padding: 2px 6px;
  margin-left: auto;
}

</style>
