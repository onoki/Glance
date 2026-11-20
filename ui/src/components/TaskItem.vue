<template>
  <div
    ref="taskRoot"
    class="task-item"
    @focusin="trackFocusOverlay"
    @click="handleTaskClick"
    @keydown="actionDismissal.interact($event)"
    @keydown.esc="closeTaskMenus"
    :class="{ completed: !!task.completedAt, 'task-highlight': highlightFlash, 'has-task-selector': !!clipboardAdapter && !readOnly, 'whole-task-selected': wholeTaskSelected, 'task-group-hover': groupHovered, 'actions-dismissed': actionsDismissed, 'task-dragging': dragging }"
    :draggable="false"
    :data-task-id="task.id"
    :tabindex="privateSubcontent ? 0 : undefined"
    @dragstart="handleDragStart"
    @dragover.capture="handleDragOver"
    @dragenter.capture="handleDragOver"
    @dragleave="handleDragLeave"
    @drop.capture="handleDrop"
    @dragend="handleDragEnd"
  >
    <div
      v-if="isDropTarget"
      class="drop-indicator"
      :class="{ after: dropPosition === 'after' }"
    ></div>
    <button
      v-if="groupMembers.length" type="button" class="task-group-handle"
      :class="{ 'group-start': groupFirst, 'group-end': groupLast }"
      :aria-label="`Select group of ${groupMembers.length} tasks`" :aria-pressed="groupSelected"
      title="Click to select the group · Drag to move it"
      :tabindex="groupFirst ? 0 : -1" :draggable="draggable && !clipboardBusy" :disabled="clipboardBusy"
      @pointerenter="hoverGroup" @pointerleave="clearGroupHover"
      @click="selectGroup()" @dragstart.stop="handleDragStart" @dragend.stop="handleDragEnd"
    ></button>
    <button
      v-if="clipboardAdapter && !readOnly" ref="selectionHandle" type="button"
      class="task-select-handle" aria-label="Select whole task" :aria-pressed="wholeTaskSelected"
      title="Select whole task · Drag to reorder · Ctrl+click to add · Shift+click for range · Ctrl+Shift+Space from text"
      :draggable="draggable && !clipboardBusy"
      @dragstart.stop="handleDragStart" @dragend.stop="handleDragEnd"
      :disabled="clipboardBusy" @click="taskClipboard.select(task.id, $event)"
    ></button>
    <label class="task-check" :title="completionActionLabel">
      <input
        type="checkbox"
        :checked="!!task.completedAt"
        :disabled="!allowToggle || selectionActionBusy || clipboardBusy"
        :aria-label="completionActionLabel"
        @change="toggleComplete"
      />
      <span></span>
    </label>
    <div v-show="!actionsDismissed" ref="taskMeta" class="task-meta" :style="overlayStyle" :inert="actionsDismissed">
      <div class="task-meta-labels">
        <span v-if="showScheduledDate" class="task-date">{{ task.scheduledDate }}</span>
        <span v-if="creationDate" class="task-date" :title="`Created ${creationDate}`">{{ creationDate }}</span>
        <span v-if="showOwnerPerson && task.ownerPersonName" class="task-context">{{ task.ownerPersonName }}</span>
        <span v-if="task.originLabel" class="task-context">{{ task.originLabel }}</span>
      </div>
      <div class="task-action-strip" aria-label="Task actions">
        <button
          v-for="action in groupActions" :key="action" type="button" class="task-tool task-icon-button contextual-task-action"
          :aria-label="groupActionLabel(action)" :title="groupActionLabel(action)"
          :disabled="groupUpdating || clipboardBusy" @click.stop="changeGroup(action)"
        >
          {{ groupControls[action].icon }}
        </button>
        <button
          v-if="!readOnly" type="button" class="task-tool task-icon-button contextual-task-action"
          :aria-pressed="privateSelectionState" :aria-label="privacyActionLabel"
          :title="selectedCount > 1 ? privacyActionLabel : privateSubcontent ? 'Private subcontent: shown only while this task is focused when privacy is on' : 'Mark subcontent private'"
          :disabled="selectionActionBusy || clipboardBusy"
          @click.stop="togglePrivateSubcontent"
        >
          <svg width="11" height="11" viewBox="0 0 16 16" fill="none" stroke="currentColor" aria-hidden="true"><rect x="3" y="7" width="10" height="8" rx="1" /><path :d="privateSubcontent ? 'M5 7V4a3 3 0 0 1 6 0v3' : 'M5 7V4a3 3 0 0 1 6 0'" /></svg>
        </button>
        <button
          v-if="draggable && !readOnly && !clipboardAdapter"
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
        >
          <button type="button" class="category-toggle task-icon-button" :aria-label="moveLabel" :title="moveLabel" :aria-expanded="categoryOpen" @click.stop="toggleCategoryMenu">▦</button>
          <div
            v-if="categoryOpen"
            ref="categoryMenuRef"
            class="floating-menu category-menu"
            @click.stop
            :style="{ left: `${categoryMenuLeft}px`, top: `${categoryMenuTop}px` }"
          >
            <template v-for="option in categoryOptions" :key="option.id">
              <button type="button" class="category-option" @click="setCategory(option.id)">
                {{ option.label }}
              </button>
              <div v-if="categoryPickerDays(option.id).length" class="week-picker-days" :aria-label="`Schedule ${option.label.toLowerCase()}`">
                <button
                  v-for="day in categoryPickerDays(option.id)" :key="day.dateKey" type="button"
                  :disabled="day.isPast" :aria-pressed="task.scheduledDate === day.dateKey"
                  :aria-label="`Move to ${day.name} ${day.dateKey}`"
                  :title="day.isPast ? `${day.name} ${day.dateKey} has passed` : `Move to ${day.name} ${day.dateKey}`"
                  @click="setCategory(`day-${day.dateKey}`)"
                >
                  {{ day.label.slice(0, 2) }}
                </button>
              </div>
            </template>
          </div>
        </div>
        <div v-if="onSendToPeople" ref="sendPickerRef" class="task-popover-wrap contextual-task-action">
          <button type="button" class="task-tool task-icon-button" :aria-label="sendLabel" :title="sendLabel" @click.stop="toggleSendMenu">→</button>
          <div
            v-if="sendOpen"
            ref="sendMenuRef"
            class="floating-menu task-popover send-menu"
            :style="{ left: `${sendMenuLeft}px`, top: `${sendMenuTop}px` }"
            @click.stop
          >
            <strong class="task-popover-title">{{ selectedCount > 1 ? `Send copies of ${selectedCount} tasks to` : 'Send a copy to' }}</strong>
            <div class="send-recipients">
              <label v-if="allowDashboardSend" class="task-menu-option" @mousedown.prevent><input v-model="selectedDashboard" type="checkbox" /> Send to Dashboard</label>
              <label v-for="person in sendPeople" :key="person.id" class="task-menu-option" @mousedown.prevent><input v-model="selectedPeople" type="checkbox" :value="person.id" /> {{ person.displayName }}</label>
              <label v-for="tag in sendTags" :key="tag.id" class="task-menu-option" @mousedown.prevent><input v-model="selectedTags" type="checkbox" :value="tag.id" /> Everyone tagged {{ tag.name }}</label>
              <span v-if="!sendPeople.length" class="task-context">Add people in the People tab first.</span>
            </div>
            <button type="button" class="category-option send-confirm" :disabled="sending || (!selectedDashboard && !selectedPeople.length && !selectedTags.length)" @click="sendToPeople">{{ sending ? 'Sending…' : 'Send' }}</button>
          </div>
        </div>
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
            <button type="button" class="category-option" @click="dismissSendMarker">{{ selectedCount > 1 ? 'Hide sent markers for selected tasks' : 'Hide sent marker' }}</button>
          </div>
        </div>
        <button
          v-if="canDelete"
          type="button"
          class="delete-task task-icon-button contextual-task-action"
          :aria-label="deleteActionLabel"
          :title="deleteActionLabel"
          :disabled="selectionActionBusy || clipboardBusy"
          @click.stop="handleDelete"
        >
          ×
        </button>
      </div>
    </div>
    <div class="task-body">
      <div class="title-row">
        <span v-if="!readOnly" class="title-start-target" aria-hidden="true" @mousedown.prevent @click="focusTitleStart"></span>
        <RichTextEditor
          ref="titleEditorRef"
          class="title-editor"
          v-model="title"
          mode="title"
          :editable="!readOnly && !clipboardBusy"
          :on-dirty="handleTitleDirty"
          :on-split-to-new-task="noopSplit"
          :on-key-down="handleTitleKeydown"
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
      <div v-if="hasSubcontent" class="private-subcontent">
        <div :inert="privacyHidden" :aria-hidden="privacyHidden" :style="privacyHidden ? { visibility: 'hidden' } : undefined">
          <RichTextEditor
            ref="contentEditorRef"
            class="content-editor"
            v-model="content"
            :editable="!readOnly && !clipboardBusy"
            :on-dirty="handleContentDirty"
            :on-split-to-new-task="handleSplitToNewTask"
            :on-key-down="handleContentKeydown"
            :on-focus="handleContentFocus"
            :on-blur="handleContentBlur"
          />
        </div>
        <button v-if="privacyHidden" type="button" class="private-subcontent-mask" @mousedown.prevent @click.stop="revealSubcontent">Private subcontent — focus to reveal</button>
      </div>
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
import { taskClipboard } from "../services/taskClipboard.js";
import { hoveredGroupTaskId } from "../services/taskGroupHover.js";
import { privacyMode, privacyPresentationKey, revealedPrivateTask, revealPrivateTask } from "../composables/usePrivacy.js";
import { computed, inject, nextTick, onBeforeUnmount, onMounted, onUpdated, ref, watch } from "vue";
import { categoryPickerDays } from "../utils/categoryUtils.js";
import { formatDateKey } from "../utils/dateUtils.js";
import { isTaskDrag, TASK_DRAG_TYPE, taskDropPosition } from "../utils/taskDrag.js";
import { taskGroup, taskGroupActions } from "../utils/taskGroups.js";
import { createTaskActionDismissal } from "../utils/taskActionDismissal.js";
import { announceAction } from "../services/actionFeedback.js";
import RichTextEditor from "./RichTextEditor.vue";
import RecurrenceControls from "./RecurrenceControls.vue";
import { useTaskEditing } from "../composables/useTaskEditing.js";
import { indentTaskDocuments } from "../utils/taskJoin.js";
import { isDocEmptyJson } from "../utils/taskDocUtils.js";
import { TASK_HIGHLIGHT_MS } from "../utils/taskNavigation.js";
import { flushAllSaves, saveCoordinator } from "../services/saveCoordinator.js";

const props = defineProps({
  clipboardAdapter: { type: Object, default: null },
  groupRows: { type: Array, default: () => [] },
  onChangeGroup: { type: Function, default: null },
  onTaskAction: { type: Function, default: null },
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
    // A task id, or a task id plus an exact selection after restructuring.
    type: [String, Object],
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
  onMergeWithNext: { type: Function, default: null },
  onMergeToPrevious: {
    type: Function,
    default: null
  },
  onSplitToNewTask: {
    type: Function,
    required: true
  },
  onNavigateHorizontal: { type: Function, default: null },
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
  showOwnerPerson: { type: Boolean, default: true },
  sendPeople: { type: Array, default: () => [] },
  sendTags: { type: Array, default: () => [] },
  onSendToPeople: { type: Function, default: null },
  allowDashboardSend: { type: Boolean, default: false },
  onLoadSendEvents: { type: Function, default: null },
  onDismissSendMarker: { type: Function, default: null }
});

const title = ref(props.task.title);
const content = ref(props.task.content);
const privateSubcontent = ref(!!props.task.isPrivate);
const privacyEnabled = inject(privacyPresentationKey, privacyMode);
const privacyHidden = computed(() => privateSubcontent.value && privacyEnabled.value && revealedPrivateTask.value !== props.task.id);
const revealSubcontent = () => {
  taskRoot.value?.focus({ preventScroll: true });
  revealPrivateTask(props.task.id);
  actionDismissal.interact();
};
const handleTaskClick = event => {
  // Read-only titles are not contenteditable, so give their private task an
  // actual focus owner as well as revealing it visually.
  if (privateSubcontent.value && props.readOnly && !taskRoot.value?.contains(document.activeElement)) {
    taskRoot.value?.focus({ preventScroll: true });
  }
  actionDismissal.interact(event);
};
const togglePrivateSubcontent = async () => {
  try {
    if (await runSelectionAction("privacy")) return;
  } catch (error) {
    window.alert(error?.message || "Could not change privacy for the selected tasks.");
    return;
  }
  privateSubcontent.value = !privateSubcontent.value;
  markDirty();
  await saveNow();
};
const dirty = ref(false);
const saving = ref(false);
const saveError = ref(null);
const titleEditorRef = ref(null);
const focusTitleStart = () => {
  if (!clipboardBusy.value) titleEditorRef.value?.focus(1);
};
const taskRoot = ref(null);
const selectionHandle = ref(null);
const wholeTaskSelected = computed(() => taskClipboard.selected.value.has(props.task.id));
const selectedCount = computed(() => wholeTaskSelected.value ? taskClipboard.selected.value.size : 1);
const moveLabel = computed(() => selectedCount.value > 1 ? `Move ${selectedCount.value} tasks to category` : "Move to category");
const sendLabel = computed(() => selectedCount.value > 1 ? `Send copies of ${selectedCount.value} tasks` : "Send a copy");
const clipboardBusy = computed(() => taskClipboard.busy.value);
const selectionActionBusy = ref(false);
const selectedTasks = computed(() => taskClipboard.selected.value.has(props.task.id)
  ? taskClipboard.actionTasks(props.task) : [props.task]);
const privateSelectionState = computed(() => {
  if (selectedTasks.value.length < 2) return privateSubcontent.value;
  if (selectedTasks.value.every(task => task.isPrivate)) return true;
  return selectedTasks.value.some(task => task.isPrivate) ? "mixed" : false;
});
const privacyActionLabel = computed(() => selectedCount.value > 1
  ? `Make ${selectedCount.value} tasks ${privateSelectionState.value === true ? "public" : "private"}`
  : "Private subcontent");
const deleteActionLabel = computed(() => selectedCount.value > 1 ? `Delete ${selectedCount.value} tasks` : "Delete task");
const runSelectionAction = async (action, value) => {
  const tasks = taskClipboard.actionTasks(props.task);
  if (tasks.length < 2 || !props.onTaskAction) return false;
  if (selectionActionBusy.value) return true;
  const activeControl = document.activeElement;
  selectionActionBusy.value = true;
  try {
    await props.onTaskAction(tasks, action, value);
    return true;
  } finally {
    selectionActionBusy.value = false;
    await nextTick();
    if (activeControl?.isConnected && document.activeElement !== activeControl) {
      selectionHandle.value?.focus({ preventScroll: true });
      await nextTick();
      activeControl.focus({ preventScroll: true });
    }
  }
};
const groupMembers = computed(() => props.onChangeGroup ? taskGroup(props.groupRows, props.task) : []);
const groupFirst = computed(() => groupMembers.value[0]?.id === props.task.id);
const groupLast = computed(() => groupMembers.value.at(-1)?.id === props.task.id);
const groupSelected = computed(() => groupMembers.value.every(row => taskClipboard.selected.value.has(row.id)));
const groupHovered = computed(() => !clipboardBusy.value && groupMembers.value.some(row => row.id === hoveredGroupTaskId.value));
const hoverGroup = () => {
  if (!clipboardBusy.value) hoveredGroupTaskId.value = props.task.id;
};
const clearGroupHover = () => {
  if (hoveredGroupTaskId.value === props.task.id) hoveredGroupTaskId.value = null;
};
const handleWindowBlur = () => {
  clearGroupHover();
  actionDismissal.blur();
};
const groupActions = computed(() => props.onChangeGroup ? taskGroupActions(props.groupRows, props.task, taskClipboard.selected.value) : []);
const groupControls = {
  create: { label: "Group selected tasks", icon: "⊏" },
  extend: { label: "Add selected tasks to group", icon: "⊏+" },
  "attach-above": { label: "Attach to group above", icon: "+↑" },
  "attach-below": { label: "Attach to group below", icon: "+↓" },
  detach: { label: "Detach this task from group", icon: "−" }
};
const groupUpdating = ref(false);
const groupActionLabel = action => action === "detach" && selectedCount.value > 1
  ? "Detach selected tasks from groups" : groupControls[action].label;
const selectGroup = (focus = true) => taskClipboard.selectGroup(groupMembers.value.map(row => row.id), props.task.id, focus);
const changeGroup = async (action) => {
  if (groupUpdating.value) return;
  groupUpdating.value = true;
  try {
    const selectedIds = new Set(taskClipboard.selected.value);
    const changed = await props.onChangeGroup?.(props.task, selectedIds, action);
    if (!changed) return;
    if (action === "detach") taskClipboard.selectGroup(selectedIds.has(props.task.id) ? [...selectedIds] : [props.task.id], props.task.id);
    else selectGroup();
  } catch (error) {
    window.alert(error?.message || "Could not update the task group.");
  } finally {
    groupUpdating.value = false;
  }
};
let unregisterClipboard;
const taskMeta = ref(null);
const actionsDismissed = ref(false);
const dragging = ref(false);
const actionDismissal = createTaskActionDismissal((dismissed) => {
  actionsDismissed.value = dismissed;
  if (dismissed) {
    closeTaskMenus();
    // Hide synchronously: a native activation click can precede Vue's next patch.
    if (taskMeta.value) {
      taskMeta.value.style.display = 'none';
      taskMeta.value.inert = true;
    }
  } else {
    nextTick(positionTaskOverlay);
  }
});
const overlayStyle = ref({});
let overlayObserver;
let overlayFrame = null;
function trackFocusOverlay() {
  if (overlayFrame !== null) cancelAnimationFrame(overlayFrame);
  const until = performance.now() + 350;
  const follow = () => {
    overlayFrame = null;
    if (!taskRoot.value?.matches(':focus-within') || actionsDismissed.value) return;
    positionTaskOverlay();
    if (performance.now() < until) overlayFrame = requestAnimationFrame(follow);
  };
  follow();
}

function positionTaskOverlay() {
  const root = taskRoot.value;
  if (actionsDismissed.value || !root || !taskMeta.value || !root.matches(':focus-within')) return;
  const rect = root.getBoundingClientRect();
  const container = root.closest('.list-card, .person-task-list, .search-results, .history-view');
  const bounds = container?.getBoundingClientRect() || { left: 0, top: 0, right: innerWidth, bottom: innerHeight };
  const left = Math.max(0, bounds.left, rect.left);
  const right = Math.min(innerWidth, bounds.right - (container ? container.offsetWidth - container.clientWidth : 0), rect.right);
  const top = Math.max(0, rect.top - taskMeta.value.getBoundingClientRect().height);
  const nextStyle = {
    left: `${left}px`, top: `${top}px`, width: `${Math.max(0, right - left)}px`,
    display: right <= left || rect.bottom < bounds.top || rect.top > Math.min(innerHeight, bounds.bottom) ? 'none' : ''
  };
  if (Object.keys(nextStyle).some(key => overlayStyle.value[key] !== nextStyle[key])) overlayStyle.value = nextStyle;
}
const contentEditorRef = ref(null);
const forceSubcontent = ref(false);
const contentFocused = ref(false);
const highlightFlash = ref(false);
const categoryPickerRef = ref(null);
const categoryOpen = ref(false);
const toggleCategoryMenu = async () => {
  const open = !categoryOpen.value;
  closeTaskMenus();
  categoryOpen.value = open;
  await nextTick();
  updateCategoryMenuPosition();
};
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
const selectedDashboard = ref(false);
const selectedTags = ref([]);
const sendEvents = ref([]);
const sending = ref(false);
const loadingEvents = ref(false);
const sendMarkerVisible = ref(!!props.task.sendMarkerVisible);
const updatedAt = ref(props.task.updatedAt);
let saveTimer = null;
let highlightTimer = null;
let highlightFrame = null;
let saveRegistration = null;

const removeMenuListeners = () => {
  window.removeEventListener("resize", updateOpenMenuPositions);
  document.removeEventListener("scroll", updateOpenMenuPositions, true);
  document.removeEventListener("click", closeTaskMenus);
};

// A sibling deletion can move this row without resizing it or firing focusin.
onUpdated(positionTaskOverlay);

onBeforeUnmount(() => {
  clearGroupHover();
  if (saveTimer) {
    clearTimeout(saveTimer);
    saveTimer = null;
  }
  void saveRegistration?.unregister({ flush: true });
  saveRegistration = null;
  clearTimeout(highlightTimer);
  if (highlightFrame != null) cancelAnimationFrame(highlightFrame);
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
const creationDate = computed(() => {
  if (props.task.page !== 'people:main' || !Number.isFinite(props.task.createdAt)) return null;
  return formatDateKey(new Date(props.task.createdAt));
});

const canSetCategory = computed(() => !!props.onSetCategory && !props.readOnly);
const canDelete = computed(() => !!props.onDelete && props.allowDelete);
const completionActionLabel = computed(() => selectedCount.value > 1
  ? `Mark ${selectedCount.value} tasks ${selectedTasks.value.every(task => task.completedAt) ? "incomplete" : "complete"}`
  : props.task.completedAt ? "Mark task incomplete" : "Mark task complete");
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
  if (categoryOpen.value) updateCategoryMenuPosition();
  if (sendOpen.value) {
    positionFloatingMenu(sendPickerRef.value, sendMenuRef.value, sendMenuTop, sendMenuLeft);
  }
  if (eventsOpen.value) {
    positionFloatingMenu(eventsPickerRef.value, eventsMenuRef.value, eventsMenuTop, eventsMenuLeft);
  }
}

function closeTaskMenus(event) {
  if (event?.type === 'click' && [categoryPickerRef.value, sendPickerRef.value, eventsPickerRef.value]
    .some(picker => picker?.contains(event.target))) return;
  if (event?.key === 'Escape') {
    const picker = categoryOpen.value ? categoryPickerRef.value : sendOpen.value ? sendPickerRef.value : eventsOpen.value ? eventsPickerRef.value : null;
    picker?.querySelector('button')?.focus();
  }
  categoryOpen.value = false;
  sendOpen.value = false;
  eventsOpen.value = false;
}

const categoryOptions = [
  { id: "uncategorized", label: "Uncategorized" },
  { id: "this-week", label: "This week" },
  { id: "next-week", label: "Next week" },
  { id: "two-weeks", label: "In two weeks" },
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
  isPrivate: privateSubcontent.value,
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
    isPrivate: privateSubcontent.value,
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
  if (props.clipboardAdapter && !props.readOnly) {
    unregisterClipboard = taskClipboard.register(props.task.id, {
      adapter: props.clipboardAdapter, element: () => taskRoot.value,
      handle: () => selectionHandle.value, snapshot, task: () => props.task,
      deleteSelected: () => handleDelete(),
      focusEditor: () => titleEditorRef.value?.focus()
    });
  }
  window.addEventListener('resize', positionTaskOverlay);
  window.addEventListener('blur', handleWindowBlur);
  document.addEventListener('scroll', positionTaskOverlay, true);
  overlayObserver = new ResizeObserver(positionTaskOverlay);
  overlayObserver.observe(taskRoot.value);
  if (!props.readOnly) {
    saveRegistration = saveCoordinator.register(props.task.id, {
      save: ({ options }) => performSave(options),
      onStateChange: applySaveState
    });
  }
});

onBeforeUnmount(() => {
  window.removeEventListener('resize', positionTaskOverlay);
  window.removeEventListener('blur', handleWindowBlur);
  unregisterClipboard?.();
  document.removeEventListener('scroll', positionTaskOverlay, true);
  overlayObserver?.disconnect();
  if (overlayFrame !== null) cancelAnimationFrame(overlayFrame);
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
  getTaskSnapshot: snapshot,
  props,
  onDelete: handleDelete,
  revealContent: () => {
    forceSubcontent.value = true;
    if (privacyHidden.value) revealPrivateTask(props.task.id);
  },
  onContentChanged: markDirty,
  titleRef: title,
  contentRef: content,
  titleEditorRef,
  contentEditorRef,
  hasSubcontent,
  saveNow
});

const toggleComplete = async (event) => {
  if (!props.allowToggle) {
    return;
  }
  if (event?.target && selectedTasks.value.length > 1) {
    event.target.checked = !selectedTasks.value.every(task => task.completedAt);
  }
  const saved = await saveNow();
  if (!saved) {
    window.alert("Could not save the latest edits before changing completion.");
    return;
  }
  try {
    if (await runSelectionAction("complete")) return;
    await props.onComplete(snapshot());
  } catch (error) {
    if (event?.target) event.target.checked = !!props.task.completedAt;
    window.alert(error?.message || "Could not change task completion.");
  }
};

let deleting = false;
async function handleDelete(_task, options = {}) {
  if (deleting) return;
  deleting = true;
  try {
    if (await saveNow() === false) throw new Error("Could not save edits before deleting the note.");
    if (await runSelectionAction("delete")) return;
    await props.onDelete?.(snapshot(), options);
  } catch (error) { window.alert(error?.message || "Could not delete the note."); }
  finally { deleting = false; }
}

const isEditorTarget = (event) =>
  !!event?.target?.closest?.(".ProseMirror, .editor-surface");

const isHandleTarget = (event) => !!event?.target?.closest?.(".drag-handle, .task-select-handle, .task-group-handle");

const handleDragStart = (event) => {
  if (!props.draggable || !props.onDragStart) {
    return;
  }
  if (!isHandleTarget(event) || isEditorTarget(event)) {
    event.preventDefault();
    return;
  }
  dragging.value = true;
  event.dataTransfer?.setData(TASK_DRAG_TYPE, props.task.id);
  closeTaskMenus();
  const bracketDrag = event.target?.closest?.(".task-group-handle");
  if (bracketDrag) selectGroup(false);
  const tasks = bracketDrag ? groupMembers.value : taskClipboard.actionTasks(props.task);
  props.onDragStart(props.task, event, tasks);
};

const handleDragOver = (event) => {
  if (!isTaskDrag(event)) return;
  event.preventDefault();
  event.stopPropagation();
  if (dragging.value) return;
  const rect = event.currentTarget?.getBoundingClientRect();
  if (!rect) {
    return;
  }
  const position = taskDropPosition(rect, event.clientY);
  props.onDragOver?.(props.task.id, position);
};

const handleDragLeave = (event) => {
  // Moving between editor paragraphs/children is still inside the same task.
  if (event.currentTarget?.contains(event.relatedTarget)) return;
  const rect = event.currentTarget?.getBoundingClientRect();
  if (rect && event.clientX >= rect.left && event.clientX < rect.right && event.clientY >= rect.top && event.clientY < rect.bottom) return;
  props.onDragLeave?.(props.task.id);
};

const handleDrop = async (event) => {
  if (!isTaskDrag(event) || !props.onDrop) return;
  handleDragOver(event);
  event.preventDefault();
  event.stopPropagation();
  try { await props.onDrop(props.task, props.dragCategoryId, event); }
  catch (error) { window.alert(error?.message || "Could not reorder the task."); }
  finally { props.onDragEnd?.(); }
};

const handleDragEnd = () => {
  dragging.value = false;
  nextTick(positionTaskOverlay);
  props.onDragEnd?.();
};

let splittingSubcontent = false;
const handleSplitToNewTask = async (payload) => {
  if (props.readOnly || splittingSubcontent) return null;
  splittingSubcontent = true;
  const clone = value => JSON.parse(JSON.stringify(value));
  const previousContent = clone(content.value);
  const previousTitle = JSON.stringify(title.value);
  try {
    if (await saveNow() === false) throw new Error("Could not save edits before splitting the note.");
    return await props.onSplitToNewTask(snapshot(), {
      ...payload, previousContent, categoryId: props.categoryId,
      commitSource: async () => {
        if (JSON.stringify(content.value) !== JSON.stringify(previousContent) || JSON.stringify(title.value) !== previousTitle) {
          throw new Error('The note changed while splitting. Both versions have been kept; review the new copy.');
        }
        content.value = clone(payload.remainingContent);
        markDirty();
        await nextTick();
        const shortenedSnapshot = JSON.stringify(content.value);
        const saved = await saveNow(true, { suppressUndo: true });
        if (saved === false) {
          content.value = JSON.stringify(content.value) === shortenedSnapshot ? previousContent
            : indentTaskDocuments({ content: content.value }, { title: payload.originalTitle ?? payload.title, content: payload.content }).content;
          markDirty();
        }
        return saved;
      }
    });
  } catch (error) {
    window.alert(error?.message || 'Could not split the note. The original content has been kept.');
    return null;
  } finally { splittingSubcontent = false; }
};

const noopSplit = () => {};

const setCategory = async (category) => {
  if (props.readOnly || !props.onSetCategory) {
    return;
  }
  closeTaskMenus();
  const tasks = taskClipboard.actionTasks(props.task);
  try { await props.onSetCategory(tasks.length > 1 ? tasks : props.task, category); }
  catch (error) { window.alert(error?.message || "Could not move the task."); }
};

const toggleSendMenu = async () => {
  categoryOpen.value = false;
  eventsOpen.value = false;
  sendOpen.value = !sendOpen.value;
  if (sendOpen.value) {
    await nextTick();
    updateOpenMenuPositions();
  }
};

const sendToPeople = async () => {
  if (sending.value) return;
  const tasks = taskClipboard.actionTasks(props.task);
  const personIds = [...selectedPeople.value];
  const tagIds = [...selectedTags.value];
  const includeDashboard = selectedDashboard.value;
  sending.value = true;
  try {
    const saved = tasks.length > 1 ? (await flushAllSaves()).ok : await saveNow();
    if (!saved) throw new Error("Could not save the latest edits before copying the task.");
    const response = await props.onSendToPeople(tasks.length > 1 ? tasks : props.task, personIds, tagIds, includeDashboard);
    if (response?.created === 0) throw new Error("No other active people matched. Choose another person or tag.");
    announceAction(tasks.length > 1 ? `Sent copies of ${tasks.length} tasks.` : 'Sent a copy to the selected destinations.');
    sendMarkerVisible.value = true;
    selectedPeople.value = [];
    selectedTags.value = [];
    selectedDashboard.value = false;
    sendOpen.value = false;
  } catch (error) {
    window.alert(error?.message || "Could not copy the task");
  } finally { sending.value = false; }
};

const toggleEvents = async () => {
  categoryOpen.value = false;
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
  try {
    if (!(await runSelectionAction("dismiss-send"))) await props.onDismissSendMarker?.(props.task);
    sendMarkerVisible.value = false;
    eventsOpen.value = false;
  } catch (error) {
    window.alert(error?.message || "Could not hide sent markers.");
  }
};

const formatEventTime = (milliseconds) => new Date(milliseconds).toLocaleString();

watch(
  () => categoryOpen.value || sendOpen.value || eventsOpen.value,
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

const applyRecurrence = async () => {
  if (props.readOnly || !props.onSetRecurrence) {
    return;
  }
  try {
    const recurrence = buildRecurrencePayload();
    if (await runSelectionAction("recurrence", recurrence)) return;
    await props.onSetRecurrence(props.task, recurrence);
  } catch (error) {
    window.alert(error?.message || "Could not change recurrence.");
  }
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
  async (target) => {
    const id = typeof target === 'string' ? target : target?.taskId;
    if (!id || id !== props.task.id) {
      return;
    }
    await nextTick();
    // Explicit editing navigation (Delete/merge/etc.) may resume controls on
    // the destination. Native window focus restoration still cannot do this.
    actionDismissal.interact();
    titleEditorRef.value?.focus(target?.selection, target?.vertical);
    await nextTick();
    positionTaskOverlay();
  },
  { immediate: true }
);

watch(
  () => props.focusContentTarget,
  async (target) => {
    if (!target || target.taskId !== props.task.id) {
      return;
    }
    forceSubcontent.value = true;
    // Remove inert before ProseMirror measures or focuses this destination.
    if (privacyHidden.value) revealPrivateTask(props.task.id);
    await nextTick();
    actionDismissal.interact();
    if (target.selection) contentEditorRef.value?.focus(target.selection, target.vertical);
    else contentEditorRef.value?.focusListItem(target.listIndex, target.atEnd ? "end" : "start");
    await nextTick();
    positionTaskOverlay();
  },
  { immediate: true }
);

watch(
  () => [props.highlightId, props.highlightNonce],
  ([id]) => {
    highlightFlash.value = false;
    clearTimeout(highlightTimer);
    if (highlightFrame != null) cancelAnimationFrame(highlightFrame);
    if (!id || id !== props.task.id) return;
    highlightFrame = requestAnimationFrame(() => {
      highlightFrame = null;
      highlightFlash.value = true;
      highlightTimer = setTimeout(() => {
        highlightFlash.value = false;
      }, TASK_HIGHLIGHT_MS);
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
    privateSubcontent.value = !!props.task.isPrivate;
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
    privateSubcontent.value = !!task.isPrivate;
    updatedAt.value = task.updatedAt;
    sendMarkerVisible.value = !!task.sendMarkerVisible;
  },
  { deep: true }
);
</script>

<style scoped>
.task-item {
  --task-title-line-height: calc(var(--font-size-title) * var(--line-height-body));
  display: grid;
  grid-template-columns: 12px minmax(0, 1fr);
  grid-template-rows: auto;
  column-gap: 2px;
  row-gap: 0;
  padding: 4px 0;
  border: none;
  background: transparent;
  align-items: start;
  position: relative;
}

.task-item.has-task-selector { grid-template-columns: 8px 12px minmax(0, 1fr); }
.has-task-selector .task-check { grid-column: 2; }
.has-task-selector .task-body { grid-column: 3; }
.task-item.whole-task-selected { background: var(--bg-tab-active); outline: 1px solid var(--focus-outline); }

#app .task-item.has-task-selector { padding-inline-start: 8px; }
#app .task-group-handle {
  --task-group-stroke: 2px;
  position: absolute;
  inset: 0 auto calc(-1 * var(--task-row-gap, 2px)) 1px;
  width: 7px;
  min-width: 0;
  min-height: 0;
  padding: 0;
  border: 0;
  border-radius: 0;
  background: transparent;
  color: var(--text-muted);
  cursor: move;
}
#app .task-group-handle::after {
  content: "";
  position: absolute;
  inset: 0 0 0 2px;
  border-inline-start: var(--task-group-stroke) solid currentColor;
  pointer-events: none;
}
#app .task-group-handle.group-start::after { border-block-start: var(--task-group-stroke) solid currentColor; }
#app .task-group-handle.group-end { bottom: 0; }
#app .task-group-handle.group-end::after { border-block-end: var(--task-group-stroke) solid currentColor; }
#app .task-group-handle:not(:disabled):hover,
#app .task-group-handle[aria-pressed="true"] { color: var(--text-main); }
#app .task-group-hover:not(.whole-task-selected) { background: var(--bg-tab-hover); }
#app .task-group-hover .task-group-handle:not(:disabled) {
  color: var(--focus-outline);
  background: var(--border-panel);
}

.task-item.task-highlight {
  background: transparent;
  animation: task-flash 5.8s ease-out;
}

@keyframes task-flash {
  0%, 86% {
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
  margin-top: max(0px, calc((var(--task-title-line-height) - 12px) / 2));
  position: relative;
  cursor: pointer;
  width: 12px;
  height: 12px;
  grid-row: 1;
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
  position: fixed;
  z-index: 20;
  background: transparent;
  visibility: hidden;
  pointer-events: none;
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 3px;
  min-width: 0;
  min-height: 18px;
  font-size: var(--font-size-body);
  color: var(--text-muted);
}

.task-item:focus-within .task-meta { visibility: visible; }

.task-meta .task-date { background: var(--bg-panel); margin: 0; top: 0; padding: 0 2px; }
.task-meta-labels > span { background: var(--bg-panel); pointer-events: auto; }
.task-action-strip button, .task-action-strip .floating-menu { pointer-events: auto; }

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
  flex: 0 1 auto;
  flex-wrap: wrap;
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
  font-size: var(--font-size-body);
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

.task-item:focus-within .contextual-task-action {
  visibility: visible;
  pointer-events: auto;
}

.sent-mark { color: var(--text-main); font-weight: 400; }
.task-context {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: var(--font-size-body);
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
  font-size: var(--font-size-body);
}

.task-popover-title { font-size: var(--font-size-body); font-weight: 400; color: var(--text-muted); }
.task-popover.send-menu { overflow: hidden; }
.send-menu > .task-popover-title, .send-confirm { flex: 0 0 auto; }
.send-recipients { min-height: 0; overflow-y: auto; display: flex; flex-direction: column; gap: 4px; }
.task-menu-option { display: flex; align-items: center; gap: 4px; min-height: 18px; }
.task-menu-option input { margin: 0; }

.delete-task {
  color: var(--text-muted);
}

.task-body {
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 0;
  position: relative;
  grid-row: 1;
  grid-column: 2;
}

.title-row {
  display: flex;
  align-items: flex-start;
  gap: 2px;
  padding-right: 10px;
  padding-top: max(0px, calc((12px - var(--task-title-line-height)) / 2));
  position: relative;
}

.title-editor {
  flex: 1;
}

.title-row:has(.title-start-target) {
  margin-left: 2px;
}

.title-start-target {
  position: absolute;
  inset: 0 auto 0 -4px;
  width: 4px;
  cursor: text;
}

.title-editor :deep(.editor-surface) {
  padding: 0;
}

.title-editor :deep(.ProseMirror) {
  /* Typography is shared with subcontent in controls.css. */
  min-height: 1.2em;
}

.content-editor {
  margin-top: -2px;
}

.private-subcontent { position: relative; display: flow-root; }
#app button.private-subcontent-mask {
  position: absolute; inset: 0; width: 100%; height: 100%; min-height: 0;
  padding: 0 3px; text-align: left; border: 0;
  background: var(--bg-tab-active); color: var(--text-muted); cursor: pointer;
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
}

.dirty-indicator {
  position: absolute;
  right: 1px;
  top: 2px;
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--text-warning);
  pointer-events: none;
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
  font-size: var(--font-size-body);
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
  pointer-events: auto;
  transform: none;
}

.category-option {
  border: 1px solid var(--border-panel);
  background: var(--bg-panel);
  color: var(--text-main);
  border-radius: 0;
  font-size: var(--font-size-body);
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
