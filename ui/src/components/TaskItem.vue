<template>
  <div
    class="task-item"
    :class="{ completed: !!task.completedAt, 'task-highlight': highlightFlash }"
    :draggable="false"
    :data-task-id="task.id"
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
    <label class="task-check" :title="completionActionLabel">
      <input
        type="checkbox"
        :checked="!!task.completedAt"
        :disabled="!allowToggle"
        :aria-label="completionActionLabel"
        @change="toggleComplete"
      />
      <span></span>
    </label>
    <div class="task-meta">
      <div class="task-meta-labels">
        <span v-if="showScheduledDate" class="task-date">{{ task.scheduledDate }}</span>
        <span v-if="showOwnerPerson && task.ownerPersonName" class="task-context">{{ task.ownerPersonName }}</span>
        <span v-if="task.originLabel" class="task-context">{{ task.originLabel }}</span>
      </div>
      <div class="task-action-strip" aria-label="Task actions">
        <button
          v-if="draggable && !readOnly"
          type="button"
          class="drag-handle task-icon-button contextual-task-action"
          aria-label="Reorder task"
          title="Reorder task"
          :draggable="true"
          @dragstart.stop="handleDragStart"
          @dragend.stop="handleDragEnd"
        >
          ↕
        </button>
        <div
          v-if="canSetCategory"
          ref="categoryPickerRef"
          class="category-picker contextual-task-action"
          @mouseenter="updateCategoryMenuPosition"
          @focusin="updateCategoryMenuPosition"
        >
          <button type="button" class="category-toggle task-icon-button" aria-label="Change category" title="Change category">▦</button>
          <div
            ref="categoryMenuRef"
            class="floating-menu category-menu"
            :style="{ left: `${categoryMenuLeft}px`, top: `${categoryMenuTop}px` }"
          >
            <button
              v-for="option in categoryOptions"
              :key="option.id"
              type="button"
              class="category-option"
              @click="setCategory(option.id)"
            >
              {{ option.label }}
            </button>
          </div>
        </div>
        <button
          v-if="allowStatusMarkers && showStatusMarkerButton"
          type="button"
          class="task-tool task-icon-button status-tool contextual-task-action"
          :class="{ active: taskHasStatusMarker }"
          :aria-label="statusActionLabel"
          :aria-pressed="taskHasStatusMarker"
          :title="statusActionLabel"
          @click.stop="toggleStatusButton"
        >
          📝
        </button>
        <div v-if="onSendToPeople" ref="sendPickerRef" class="task-popover-wrap contextual-task-action">
          <button type="button" class="task-tool task-icon-button" aria-label="Send a copy to people" title="Send a copy to people" @click.stop="toggleSendMenu">→</button>
          <div
            v-if="sendOpen"
            ref="sendMenuRef"
            class="floating-menu task-popover"
            :style="{ left: `${sendMenuLeft}px`, top: `${sendMenuTop}px` }"
            @click.stop
          >
            <strong class="task-popover-title">Send to people</strong>
            <label v-for="person in sendPeople" :key="person.id" class="task-menu-option"><input v-model="selectedPeople" type="checkbox" :value="person.id" /> {{ person.displayName }}</label>
            <label v-for="tag in sendTags" :key="tag.id" class="task-menu-option"><input v-model="selectedTags" type="checkbox" :value="tag.id" /> Everyone tagged {{ tag.name }}</label>
            <span v-if="!sendPeople.length" class="task-context">Add people in the People tab first.</span>
            <button type="button" class="category-option" :disabled="sending || (!selectedPeople.length && !selectedTags.length)" @click="sendToPeople">Send</button>
          </div>
        </div>
        <button
          v-if="onSendToDashboard"
          type="button"
          class="task-tool task-icon-button contextual-task-action"
          aria-label="Send a copy to Dashboard"
          title="Send a copy to Dashboard"
          :disabled="sending"
          @click.stop="sendToDashboard"
        >
          →
        </button>
        <div v-if="onLoadSendEvents" ref="eventsPickerRef" class="task-popover-wrap sent-marker-slot">
          <button
            v-if="sendMarkerVisible"
            type="button"
            class="task-tool task-icon-button sent-mark"
            aria-label="Show where this task was sent"
            title="Show where this task was sent"
            @click.stop="toggleEvents"
          >
            ↗
          </button>
          <div
            v-if="eventsOpen"
            ref="eventsMenuRef"
            class="floating-menu task-popover"
            :style="{ left: `${eventsMenuLeft}px`, top: `${eventsMenuTop}px` }"
            @click.stop
          >
            <strong class="task-popover-title">Send history</strong>
            <span v-if="loadingEvents">Loading…</span>
            <span v-for="eventItem in sendEvents" :key="eventItem.id">{{ eventItem.destinationLabel }} · {{ formatEventTime(eventItem.sentAt) }}</span>
            <button type="button" class="category-option" @click="dismissSendMarker">Hide sent marker</button>
          </div>
        </div>
        <button
          v-if="canDelete"
          type="button"
          class="delete-task task-icon-button contextual-task-action"
          aria-label="Delete task"
          title="Delete task"
          @click.stop="handleDelete"
        >
          ×
        </button>
      </div>
    </div>
    <div class="task-body">
      <div class="title-row">
        <span class="title-bullet" aria-hidden="true">•</span>
        <RichTextEditor
          ref="titleEditorRef"
          class="title-editor"
          v-model="title"
          mode="title"
          :editable="!readOnly"
          :on-dirty="handleTitleDirty"
          :on-split-to-new-task="noopSplit"
          :on-key-down="handleTitleKeydown"
          :allow-status-markers="allowStatusMarkers"
        />
        <span
          v-if="dirty && !readOnly"
          class="dirty-indicator"
          :class="{ saving }"
          :aria-label="saving ? 'Saving changes' : 'Unsaved changes'"
        ></span>
      </div>
      <RecurrenceControls
        v-if="showRecurrenceControls && !readOnly"
        v-model:recurrence-type="recurrenceType"
        v-model:month-days-input="monthDaysInput"
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
        :on-focus="handleContentFocus"
        :on-blur="handleContentBlur"
        :allow-status-markers="allowStatusMarkers"
      />
      <div v-if="saveError && !readOnly" class="task-save-warning" role="alert">
        <span>{{ saveErrorMessage }}</span>
        <button
          v-if="saveError.kind === 'conflict' && saveError.currentUpdatedAt"
          type="button"
          @click="saveMyVersion"
        >
          Save my version
        </button>
        <button v-else type="button" @click="retrySave">Retry</button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from "vue";
import RichTextEditor from "./RichTextEditor.vue";
import RecurrenceControls from "./RecurrenceControls.vue";
import { useTaskEditing } from "../composables/useTaskEditing.js";
import { isDocEmptyJson } from "../utils/taskDocUtils.js";
import { hasStatusMarker, toggleWholeTaskStatus } from "../utils/statusMetadata.js";
import { saveCoordinator } from "../services/saveCoordinator.js";

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
  allowDelete: {
    type: Boolean,
    default: true
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
  highlightId: {
    type: String,
    default: null
  },
  highlightNonce: {
    type: Number,
    default: 0
  },
  undoSignal: {
    type: Number,
    default: 0
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
  onSplitTitleToNewTask: {
    type: Function,
    default: null
  },
  onTabToPrevious: {
    type: Function,
    required: true
  },
  onMergeToPrevious: {
    type: Function,
    default: null
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
  },
  allowStatusMarkers: { type: Boolean, default: false },
  showStatusMarkerButton: { type: Boolean, default: false },
  showOwnerPerson: { type: Boolean, default: true },
  onStatusMarkers: { type: Function, default: null },
  sendPeople: { type: Array, default: () => [] },
  sendTags: { type: Array, default: () => [] },
  onSendToPeople: { type: Function, default: null },
  onSendToDashboard: { type: Function, default: null },
  onLoadSendEvents: { type: Function, default: null },
  onDismissSendMarker: { type: Function, default: null }
});

const title = ref(props.task.title);
const content = ref(props.task.content);
const dirty = ref(false);
const saving = ref(false);
const saveError = ref(null);
const titleEditorRef = ref(null);
const contentEditorRef = ref(null);
const forceSubcontent = ref(false);
const contentFocused = ref(false);
const highlightFlash = ref(false);
const categoryPickerRef = ref(null);
const categoryMenuRef = ref(null);
const categoryMenuTop = ref(0);
const categoryMenuLeft = ref(0);
const sendPickerRef = ref(null);
const sendMenuRef = ref(null);
const sendMenuTop = ref(0);
const sendMenuLeft = ref(0);
const eventsPickerRef = ref(null);
const eventsMenuRef = ref(null);
const eventsMenuTop = ref(0);
const eventsMenuLeft = ref(0);
const recurrenceType = ref("");
const weeklyDays = ref([]);
const monthDaysInput = ref("");
const sendOpen = ref(false);
const eventsOpen = ref(false);
const selectedPeople = ref([]);
const selectedTags = ref([]);
const sendEvents = ref([]);
const sending = ref(false);
const loadingEvents = ref(false);
const sendMarkerVisible = ref(!!props.task.sendMarkerVisible);
const updatedAt = ref(props.task.updatedAt);
let saveTimer = null;
let highlightTimer = null;
let saveRegistration = null;

const removeMenuListeners = () => {
  window.removeEventListener("resize", updateOpenMenuPositions);
  document.removeEventListener("scroll", updateOpenMenuPositions, true);
  document.removeEventListener("click", closeTaskMenus);
};

onBeforeUnmount(() => {
  if (saveTimer) {
    clearTimeout(saveTimer);
    saveTimer = null;
  }
  void saveRegistration?.unregister({ flush: true });
  saveRegistration = null;
  if (highlightTimer) {
    clearTimeout(highlightTimer);
    highlightTimer = null;
  }
  removeMenuListeners();
});

const hasSubcontent = computed(() => {
  const doc = content.value;
  const first = doc?.content?.[0];
  if (!first || first.type !== "bulletList" || !Array.isArray(first.content)) {
    return false;
  }
  const hasContent = first.content.some((item) => !isDocEmptyJson(item));
  return hasContent || forceSubcontent.value || contentFocused.value;
});

const showScheduledDate = computed(
  () => !!props.task.scheduledDate && props.task.scheduledDate !== "no-date"
);

const canSetCategory = computed(() => !!props.onSetCategory && !props.readOnly);
const canDelete = computed(() => !!props.onDelete && props.allowDelete);
const completionActionLabel = computed(() => props.task.completedAt ? "Mark task incomplete" : "Mark task complete");
const taskHasStatusMarker = computed(() => hasStatusMarker(title.value) || hasStatusMarker(content.value));
const statusActionLabel = computed(() => taskHasStatusMarker.value
  ? "Remove entire task from project status input"
  : "Include entire task in project status input");
const saveErrorMessage = computed(() => {
  if (saveError.value?.kind === "conflict") {
    return "This note changed in another window. Your edits are preserved here. Review the other window, then save your version only if it should replace the newer saved version.";
  }
  if (saveError.value?.kind === "network") {
    return "Glance could not reach its local server. Your edits are preserved here.";
  }
  return saveError.value?.message || "Glance could not save this note. Your edits are preserved here.";
});

const positionFloatingMenu = (picker, menu, topRef, leftRef) => {
  requestAnimationFrame(() => {
    if (!picker || !menu) return;
    const pickerRect = picker.getBoundingClientRect();
    const menuRect = menu.getBoundingClientRect();
    const gap = 3;
    const viewportWidth = document.documentElement.clientWidth;
    const viewportHeight = document.documentElement.clientHeight;
    let top = pickerRect.bottom + gap;
    if (top + menuRect.height > viewportHeight - gap && pickerRect.top - gap - menuRect.height >= gap) {
      top = pickerRect.top - gap - menuRect.height;
    }
    top = Math.max(gap, Math.min(top, viewportHeight - gap - menuRect.height));

    const visibleContainer = picker.closest(".list-card, .person-task-list");
    const containerRect = visibleContainer?.getBoundingClientRect();
    const scrollbarWidth = visibleContainer
      ? Math.max(0, visibleContainer.offsetWidth - visibleContainer.clientWidth)
      : 0;
    const visibleRight = containerRect
      ? Math.min(viewportWidth - gap, containerRect.right - scrollbarWidth - gap)
      : viewportWidth - gap;
    let left = pickerRect.left;
    if (left + menuRect.width > visibleRight) left = visibleRight - menuRect.width;
    left = Math.max(gap, Math.min(left, viewportWidth - gap - menuRect.width));
    topRef.value = Math.round(top);
    leftRef.value = Math.round(left);
  });
};

const updateCategoryMenuPosition = () => {
  if (props.readOnly) return;
  positionFloatingMenu(categoryPickerRef.value, categoryMenuRef.value, categoryMenuTop, categoryMenuLeft);
};

function updateOpenMenuPositions() {
  if (sendOpen.value) {
    positionFloatingMenu(sendPickerRef.value, sendMenuRef.value, sendMenuTop, sendMenuLeft);
  }
  if (eventsOpen.value) {
    positionFloatingMenu(eventsPickerRef.value, eventsMenuRef.value, eventsMenuTop, eventsMenuLeft);
  }
}

function closeTaskMenus() {
  sendOpen.value = false;
  eventsOpen.value = false;
}

const categoryOptions = [
  { id: "uncategorized", label: "Uncategorized" },
  { id: "this-week", label: "This week" },
  { id: "next-week", label: "Next week" },
  { id: "no-date", label: "No date" },
  { id: "notes", label: "Notes" },
  { id: "repeatable", label: "Repeatable" }
];

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
  content: content.value,
  updatedAt: updatedAt.value
});

const markDirty = () => {
  saveRegistration?.markDirty();
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

const performSave = async (options = null) => {
  if (!title.value || !title.value.content || title.value.content.length === 0) {
    title.value = {
      type: "doc",
      content: [{ type: "paragraph" }]
    };
  }
  const response = await props.onSave({
    id: props.task.id,
    title: title.value,
    content: content.value,
    baseUpdatedAt: updatedAt.value,
    page: props.task.page,
    suppressUndo: options?.suppressUndo ?? false
  });
  if (response?.updatedAt) updatedAt.value = response.updatedAt;
  return response;
};

const applySaveState = (state) => {
  dirty.value = state.dirty;
  saving.value = state.saving || state.pending > 0;
  saveError.value = state.error;
  props.onDirty(props.task.id, state.dirty, state.dirty ? snapshot() : null);
};

// A category move can set up the destination before unmounting the source.
// Mounted hooks run after that patch, once the old save owner has detached.
onMounted(() => {
  if (!props.readOnly) {
    saveRegistration = saveCoordinator.register(props.task.id, {
      save: ({ options }) => performSave(options),
      onStateChange: applySaveState
    });
  }
});

const saveNow = async (force = false, options = null) => {
  if (props.readOnly) {
    return true;
  }
  if (!dirty.value && !force) {
    return true;
  }
  if (force && !dirty.value) {
    markDirty();
  }
  if (saveTimer) {
    clearTimeout(saveTimer);
    saveTimer = null;
  }
  return saveRegistration?.flush(options) ?? false;
};

const retrySave = () => {
  void saveNow();
};

const saveMyVersion = () => {
  const currentUpdatedAt = saveError.value?.currentUpdatedAt;
  if (!Number.isFinite(currentUpdatedAt)) return;
  updatedAt.value = currentUpdatedAt;
  void saveNow();
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

const handleContentFocus = () => {
  contentFocused.value = true;
  forceSubcontent.value = true;
};

const handleContentBlur = () => {
  contentFocused.value = false;
  const doc = content.value;
  const first = doc?.content?.[0];
  const hasContent = first?.type === "bulletList"
    && Array.isArray(first.content)
    && first.content.some((item) => !isDocEmptyJson(item));
  if (!hasContent) {
    forceSubcontent.value = false;
  }
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

const toggleComplete = async () => {
  if (!props.allowToggle) {
    return;
  }
  const saved = await saveNow();
  if (!saved) {
    window.alert("Could not save the latest edits before changing completion.");
    return;
  }
  try {
    await props.onComplete(snapshot());
  } catch (error) {
    window.alert(error?.message || "Could not change task completion.");
  }
};

const handleDelete = () => {
  props.onDelete?.(props.task);
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
  const previousContent = content.value;
  if (payload?.remainingContent) {
    content.value = payload.remainingContent;
    markDirty();
  }
  const saved = await saveNow(false, { suppressUndo: true });
  if (!saved) {
    window.alert("Could not save the latest edits before splitting the task.");
    return;
  }
  props.onSplitToNewTask(props.task, {
    title: payload?.title,
    content: payload?.content,
    previousContent,
    remainingContent: payload?.remainingContent,
    categoryId: props.categoryId
  });
};

const noopSplit = () => {};

const setCategory = (category) => {
  if (props.readOnly || !props.onSetCategory) {
    return;
  }
  props.onSetCategory(props.task, category);
};

const toggleStatusButton = async () => {
  if (!props.onStatusMarkers) return;
  const next = toggleWholeTaskStatus(title.value, content.value);
  try {
    const result = await props.onStatusMarkers(props.task, next.title, next.content);
    title.value = next.title;
    content.value = next.content;
    if (result?.updatedAt) updatedAt.value = result.updatedAt;
  } catch (error) {
    window.alert(error?.message || "Could not update the status marker");
  }
};

const toggleSendMenu = async () => {
  eventsOpen.value = false;
  sendOpen.value = !sendOpen.value;
  if (sendOpen.value) {
    await nextTick();
    updateOpenMenuPositions();
  }
};

const sendToPeople = async () => {
  sending.value = true;
  try {
    const saved = await saveNow();
    if (!saved) throw new Error("Could not save the latest edits before copying the task.");
    await props.onSendToPeople(props.task, selectedPeople.value, selectedTags.value);
    sendMarkerVisible.value = true;
    selectedPeople.value = [];
    selectedTags.value = [];
    sendOpen.value = false;
  } catch (error) {
    window.alert(error?.message || "Could not copy the task");
  } finally { sending.value = false; }
};

const sendToDashboard = async () => {
  sending.value = true;
  try {
    const saved = await saveNow();
    if (!saved) throw new Error("Could not save the latest edits before copying the task.");
    await props.onSendToDashboard(props.task);
    sendMarkerVisible.value = true;
  } catch (error) {
    window.alert(error?.message || "Could not copy the task");
  } finally { sending.value = false; }
};

const toggleEvents = async () => {
  sendOpen.value = false;
  eventsOpen.value = !eventsOpen.value;
  if (!eventsOpen.value || !props.onLoadSendEvents) return;
  await nextTick();
  updateOpenMenuPositions();
  loadingEvents.value = true;
  try {
    const response = await props.onLoadSendEvents(props.task);
    sendEvents.value = response?.events || [];
  } finally {
    loadingEvents.value = false;
    await nextTick();
    updateOpenMenuPositions();
  }
};

const dismissSendMarker = async () => {
  await props.onDismissSendMarker?.(props.task);
  sendMarkerVisible.value = false;
  eventsOpen.value = false;
};

const formatEventTime = (milliseconds) => new Date(milliseconds).toLocaleString();

watch(
  () => sendOpen.value || eventsOpen.value,
  async (open) => {
    removeMenuListeners();
    if (!open) return;
    window.addEventListener("resize", updateOpenMenuPositions);
    document.addEventListener("scroll", updateOpenMenuPositions, true);
    document.addEventListener("click", closeTaskMenus);
    await nextTick();
    updateOpenMenuPositions();
  }
);

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
    forceSubcontent.value = true;
    await nextTick();
    contentEditorRef.value?.focusListItem(target.listIndex, target.atEnd ? "end" : "start");
  }
);

watch(
  () => [props.highlightId, props.highlightNonce],
  ([id]) => {
    if (!id || id !== props.task.id) {
      return;
    }
    highlightFlash.value = false;
    if (highlightTimer) {
      clearTimeout(highlightTimer);
    }
    requestAnimationFrame(() => {
      highlightFlash.value = true;
      highlightTimer = setTimeout(() => {
        highlightFlash.value = false;
      }, 1000);
    });
  },
  { immediate: true }
);

watch(
  () => props.undoSignal,
  () => {
    if (saveTimer) {
      clearTimeout(saveTimer);
      saveTimer = null;
    }
    saveRegistration?.discard();
    title.value = props.task.title;
    content.value = props.task.content;
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
    updatedAt.value = task.updatedAt;
    sendMarkerVisible.value = !!task.sendMarkerVisible;
  },
  { deep: true }
);
</script>

<style scoped>
.task-item {
  display: grid;
  grid-template-columns: 12px 1fr;
  grid-template-rows: auto auto;
  column-gap: 2px;
  row-gap: 2px;
  padding: 4px 0;
  border: none;
  background: transparent;
  align-items: start;
  position: relative;
}

.task-item.task-highlight {
  background: transparent;
  animation: task-flash 0.6s ease-out;
}

@keyframes task-flash {
  0% {
    background-color: rgba(225, 199, 128, 0.5);
    box-shadow: 0 0 0 1px rgba(120, 92, 40, 0.25);
  }
  100% {
    background-color: transparent;
    box-shadow: none;
  }
}

.drop-indicator {
  position: absolute;
  top: 0;
  left: 12px;
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

.task-item.completed .title-editor :deep(.ProseMirror) {
  color: var(--text-muted);
  text-decoration: line-through;
}

.task-check {
  display: grid;
  place-items: center;
  align-self: flex-start;
  padding-top: 1px;
  position: relative;
  cursor: pointer;
  width: 12px;
  height: 12px;
  grid-row: 2;
  grid-column: 1;
}

.task-check input {
  position: absolute;
  opacity: 0;
  inset: 0;
  width: 100%;
  height: 100%;
  margin: 0;
  cursor: pointer;
  z-index: 1;
}

.task-check span {
  width: 12px;
  height: 12px;
  border: 1px solid var(--text-muted);
  border-radius: 0;
  display: inline-block;
  position: relative;
  pointer-events: none;
}

.task-check input:checked + span::after {
  content: "";
  position: absolute;
  inset: 2px;
  background: var(--text-main);
  border-radius: 0;
}

.task-meta {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 3px;
  min-width: 0;
  min-height: 18px;
  font-size: 0.7rem;
  color: var(--text-muted);
  grid-column: 2;
  grid-row: 1;
}

.task-meta-labels {
  display: flex;
  align-items: center;
  gap: 4px;
  min-width: 0;
  overflow: hidden;
  white-space: nowrap;
}

.task-action-strip {
  display: inline-flex;
  align-items: center;
  justify-content: flex-end;
  gap: 2px;
  flex: 0 0 auto;
  min-height: 18px;
}

.task-date,
.category-toggle,
.drag-handle,
.task-tool,
.delete-task {
  height: 18px;
  display: inline-flex;
  align-items: center;
  font-size: 0.7rem;
  line-height: 1;
}

.task-tool,
.task-icon-button {
  border: 1px solid var(--border-panel);
  background: var(--bg-panel);
  color: var(--text-muted);
  cursor: pointer;
  border-radius: 0;
}

.task-icon-button {
  width: 18px;
  min-width: 18px;
  padding: 0;
  justify-content: center;
  text-align: center;
}

.contextual-task-action {
  visibility: hidden;
  pointer-events: none;
}

.task-item:hover .contextual-task-action,
.task-item:focus-within .contextual-task-action {
  visibility: visible;
  pointer-events: auto;
}

.status-tool.active {
  color: var(--text-main);
  background: var(--bg-tab-active);
}

.sent-mark { color: #386a4a; font-weight: 700; }
.task-context {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 0.7rem;
  color: var(--text-muted);
}
.task-popover-wrap { position: relative; }
.sent-marker-slot { width: 18px; height: 18px; }

.floating-menu {
  position: fixed;
  z-index: 1000;
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 4px;
  border: 1px solid var(--border-panel);
  border-radius: 0;
  background: var(--bg-panel);
  box-shadow: none;
  color: var(--text-main);
}

.task-popover {
  width: 230px;
  max-height: min(360px, calc(100vh - 8px));
  overflow-y: auto;
  font-size: 0.7rem;
}

.task-popover-title { font-size: 0.7rem; font-weight: 400; color: var(--text-muted); }
.task-menu-option { display: flex; align-items: center; gap: 4px; min-height: 18px; }
.task-menu-option input { margin: 0; }

.delete-task {
  color: var(--text-muted);
}

.task-body {
  display: flex;
  flex-direction: column;
  gap: 0;
  position: relative;
  grid-row: 2;
  grid-column: 2;
}

.title-row {
  display: flex;
  align-items: flex-start;
  gap: 2px;
  padding-right: 0;
  position: relative;
}

.title-bullet {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 12px;
  color: var(--text-muted);
  font-size: 0.95rem;
  line-height: 1;
  flex: 0 0 12px;
}

.title-editor {
  flex: 1;
}

.title-editor :deep(.editor-surface) {
  padding: 0;
}

.title-editor :deep(.ProseMirror) {
  font-weight: 400;
  font-size: var(--task-title-size, 1rem);
  color: var(--text-title);
  min-height: 1.2em;
}

.content-editor {
  margin-top: -2px;
}

.dirty-indicator {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--text-warning);
  margin-left: 4px;
}

.dirty-indicator.saving {
  opacity: 0.45;
}

.task-save-warning {
  display: flex;
  align-items: flex-start;
  gap: 6px;
  margin: 3px 0 1px 14px;
  padding: 3px 5px;
  border-left: 2px solid var(--text-warning);
  color: var(--text-main);
  background: var(--bg-panel);
  font-size: 0.72rem;
  line-height: 1.25;
}

.task-save-warning span {
  flex: 1;
}

.task-save-warning button {
  flex: 0 0 auto;
  border: 1px solid var(--border-panel);
  border-radius: 0;
  padding: 2px 5px;
  background: var(--bg-panel);
  color: var(--text-main);
  font: inherit;
  cursor: pointer;
}

.task-date {
  flex: 0 0 auto;
  white-space: nowrap;
  color: var(--text-muted);
  background: transparent;
  border-radius: 0;
  padding: 0;
  margin: 0;
}

.category-picker {
  position: relative;
  display: inline-flex;
  align-items: center;
}

.category-toggle {
  color: var(--text-main);
}

.category-menu {
  opacity: 0;
  pointer-events: none;
  transform: none;
}

/* Bridge the 3px positioning gap (plus rounding) on either opening side. */
.category-menu::before {
  content: "";
  position: absolute;
  inset: -4px 0;
  z-index: -1;
}

.category-picker:hover .category-menu,
.category-picker:focus-within .category-menu {
  opacity: 1;
  pointer-events: auto;
  transform: translateY(0);
}

.category-option {
  border: 1px solid var(--border-panel);
  background: var(--bg-panel);
  color: var(--text-main);
  border-radius: 0;
  font-size: 0.7rem;
  line-height: 1;
  padding: 2px 6px;
  cursor: pointer;
  text-align: left;
  white-space: nowrap;
}

.drag-handle {
  color: var(--text-muted);
  cursor: grab;
  user-select: none;
}

.drag-handle:active {
  cursor: grabbing;
}

</style>
