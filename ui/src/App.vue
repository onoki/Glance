<template>
  <div class="app-shell">
    <header class="top-nav">
      <nav class="tabs">
        <button
          v-for="tab in tabs"
          :key="tab"
          class="tab"
          :class="{ active: activeTab === tab }"
          :disabled="startupSafety.recoveryMode && tab !== 'Settings'"
          @click="selectTab(tab)"
        >
          {{ tab }}
        </button>
      </nav>
      <div class="brand">
        <span class="brand-version">Version: {{ appVersion || "Unknown" }} UTC</span>
        <button class="tab header-icon-button" :class="{ active: activeTab === 'Settings' }" type="button" aria-label="Settings" title="Settings" :aria-pressed="activeTab === 'Settings'" @click="selectTab('Settings')">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true"><path d="m9 3 1-2h4l1 2 2 1 2-1 3 3-1 2 1 2 2 1v4l-2 1-1 2 1 2-3 3-2-1-2 1-1 2h-4l-1-2-2-1-2 1-3-3 1-2-1-2-2-1v-4l2-1 1-2-1-2 3-3 2 1z" transform="translate(1 0) scale(.9)" /><circle cx="12" cy="12" r="3" /></svg>
        </button>
        <button class="tab new-window-tab header-icon-button" type="button" aria-label="New window" title="Open another view of these notes" @click="openNewWindow">
          <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" aria-hidden="true"><path d="M5 3V1h10v10h-2M1 5h10v10H1zM3 8h6" /></svg>
        </button>
      </div>
    </header>

    <main class="content">
      <div v-if="visibleWarnings.length" class="warning-banner">
        <div v-for="warning in visibleWarnings" :key="warning.id" class="warning-item">
          <span>{{ warning.message }}</span>
          <button class="warning-dismiss" @click="dismissWarning(warning.id)">Dismiss</button>
        </div>
      </div>
      <div v-if="searchReturnContext && activeTab !== 'Search'" class="search-return-bar">
        <span>Opened from Search</span>
        <button type="button" class="ghost" @click="returnToSearch">← Back to search</button>
      </div>
      <DashboardView
        v-if="activeTab === 'Dashboard'"
        v-model:dashboard-columns-ref="dashboardColumnsRef"
        :new-tasks="newTasks"
        :main-categories="mainCategories"
        :expanded-new="expandedNew"
        :is-dashboard-dragging="isDashboardDragging"
        :get-task-item-bindings="getTaskItemBindings"
        :group-tasks-by-weekday="groupTasksByWeekday"
        :is-this-week-category="isThisWeekCategory"
        :on-move-new-to-main="moveNewToMain"
        :on-toggle-expand="toggleExpandNew"
        :on-drop-on-category="dropOnCategoryFromView"
        :on-drop-on-weekday="dropOnWeekdayFromView"
        :on-pointer-down="handleDashboardPointerDown"
        :on-pointer-move="handleDashboardPointerMove"
        :on-pointer-up="handleDashboardPointerUp"
        :on-create-new-task="createNewTaskFromEmpty"
      />

      <HistoryView
        v-else-if="activeTab === 'History'"
        :history-bars="historyBars"
        :history-scale="historyScale"
        :history-series="historySeries"
        :history-groups="historyGroups"
        :on-move-completed-to-history="moveCompletedToHistory"
        :on-complete="toggleComplete"
        :on-delete="deleteTask"
        :noop="noop"
        :noop-async="noopAsync"
        :on-status-markers="updateStatusMarkers"
        :on-load-send-events="loadTaskSendEvents"
        :on-dismiss-send-marker="dismissSendMarker"
        :navigation-target="historyNavigationTarget"
      />

      <PeopleView
        v-else-if="activeTab === 'People'"
        ref="peopleViewRef"
        :task-history="peopleTaskHistory"
        :navigation-target="peopleNavigationTarget"
        @directory-change="peopleDirectory = $event"
        @history-change="loadHistory"
      />

      <StatusUpdatesView v-else-if="activeTab === 'Status Updates'" />

      <SearchView
        v-else-if="activeTab === 'Search'"
        v-model:search-query="searchQuery"
        :search-results="searchResults"
        :has-searched="hasSearched"
        :is-searching="isSearching"
        :on-search-tasks="searchTasks"
        :search-input-ref="searchInputRef"
        :noop="noop"
        :noop-async="noopAsync"
        :on-open-source="openSearchSource"
        :navigation-target="searchNavigationTarget"
      />

      <SettingsView
        v-else-if="activeTab === 'Settings'"
        :is-backing-up="isBackingUp"
        :is-reindexing="isReindexing"
        :backup-status="backupStatus"
        :reindex-status="reindexStatus"
        :maintenance-status="maintenanceStatus"
        :app-version="appVersion"
        :is-resetting-recurrence="isResettingRecurrence"
        :recurrence-status="recurrenceStatus"
        :on-backup-now="backupNow"
        :on-reindex-search="reindexSearch"
        :on-reset-recurrence="resetRecurrenceGeneration"
        :on-prepare-data-action="prepareAllWindows"
        :on-pick-backup-folder="pickBackupFolder"
        :on-restart-for-restore="restartForPendingRestore"
      />

      <section v-else class="placeholder">
        <h2>{{ activeTab }}</h2>
        <p>This section will be implemented in the next iteration.</p>
      </section>
    </main>
  </div>
</template>

<script setup>
import { taskClipboard } from './services/taskClipboard.js';
import { createTaskClipboardAdapter } from './services/taskClipboardAdapter.js';
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from "vue";
import { moveCompletedToHistory as apiMoveCompletedToHistory } from "./api/tasks.js";
import DashboardView from "./components/views/DashboardView.vue";
import HistoryView from "./components/views/HistoryView.vue";
import SearchView from "./components/views/SearchView.vue";
import SettingsView from "./components/views/SettingsView.vue";
import PeopleView from "./components/views/PeopleView.vue";
import StatusUpdatesView from "./components/views/StatusUpdatesView.vue";
import { fetchPeople } from "./api/people.js";
import { dismissTaskSendMarker, fetchTaskSendEvents, sendTaskToPeople, setStatusMarkers } from "./api/taskSend.js";
import { groupTasksByWeekday, isThisWeekCategory } from "./utils/categoryUtils.js";
import { emptyContentDoc, emptyTitleDoc } from "./utils/taskUtils.js";
import { DASHBOARD_NEW_PAGE } from "./utils/pageConstants.js";
import { useHistory } from "./composables/useHistory.js";
import { useMaintenance } from "./composables/useMaintenance.js";
import { useSearch } from "./composables/useSearch.js";
import { useDashboardData } from "./composables/useDashboardData.js";
import { useDashboardDrag } from "./composables/useDashboardDrag.js";
import { useKeyboardShortcuts } from "./composables/useKeyboardShortcuts.js";
import { resolveSearchDestination } from "./utils/searchNavigation.js";
import { fetchStartupSafety } from "./api/dataSafety.js";
import {
  installDesktopLifecycle,
  openNewGlanceWindow,
  pickBackupFolder,
  prepareAllWindows,
  restartForPendingRestore
} from "./services/desktopLifecycle.js";

const tabs = ["Dashboard", "People", "Status Updates", "History", "Search"];
const activeTab = ref("Dashboard");
const peopleDirectory = ref({ people: [], tags: [] });
const searchReturnContext = ref(null);
const searchNavigationTarget = ref(null);
const historyNavigationTarget = ref(null);
const peopleNavigationTarget = ref(null);
const startupSafety = ref({ healthy: true, recoveryMode: false, message: "" });
let navigationNonce = 0;
let removeDesktopLifecycle = null;

const dashboardColumnsRef = ref(null);
const peopleViewRef = ref(null);

const {
  hasSearched,
  isSearching,
  searchInputRef,
  searchQuery,
  searchResults,
  searchTasks
} = useSearch();

const {
  historyBars,
  historyGroups,
  historyScale,
  historySeries,
  loadHistory
} = useHistory();

const {
  appVersion,
  backupNow,
  backupStatus,
  dismissWarning,
  isBackingUp,
  isReindexing,
  isResettingRecurrence,
  loadMaintenanceStatus,
  loadVersion,
  loadWarnings,
  maintenanceStatus,
  recurrenceStatus,
  reindexSearch,
  reindexStatus,
  resetRecurrenceGeneration,
  visibleWarnings
} = useMaintenance();

let removeTaskClipboard;
const peopleTaskHistory = { undo: [], redo: [] };
const noop = () => {};
const noopAsync = async () => false;

onMounted(async () => {
  removeDesktopLifecycle = installDesktopLifecycle();
  removeTaskClipboard = taskClipboard.install(() => activeTab.value === 'Dashboard'
    ? { adapter: dashboardClipboard, snapshot: () => ({ page: 'dashboard:new', position: 0 }) }
    : activeTab.value === 'People' ? peopleViewRef.value?.clipboardTarget?.() : null);
  try {
    startupSafety.value = await fetchStartupSafety();
  } catch {
    startupSafety.value = { healthy: true, recoveryMode: false, message: "" };
  }
  if (startupSafety.value.recoveryMode) {
    activeTab.value = "Settings";
  } else {
    await loadDashboard();
    await loadPeopleDirectory();
    if (activeTab.value === "History") {
      await loadHistory();
    }
  }
  await loadWarnings();
  await loadMaintenanceStatus();
  await loadVersion();
  maintenanceTimer = setTimeout(() => {
    loadMaintenanceStatus();
  }, 2500);
  if (!startupSafety.value.recoveryMode) {
    initDayKey();
    pollTimer = setInterval(pollChanges, 750);
    dayTimer = setInterval(handleDayTick, 60000);
  }

  window.addEventListener("keydown", handleGlobalShortcut, true);
  window.addEventListener("wheel", handleDashboardWheel, wheelOptions);
  window.addEventListener("mousewheel", handleDashboardWheel, wheelOptions);
  document.addEventListener("wheel", handleDashboardWheel, wheelOptions);
  document.addEventListener("mousewheel", handleDashboardWheel, wheelOptions);
});

onBeforeUnmount(() => {
  removeTaskClipboard?.();
  removeDesktopLifecycle?.();
  removeDesktopLifecycle = null;
  if (pollTimer) {
    clearInterval(pollTimer);
  }
  if (dayTimer) {
    clearInterval(dayTimer);
  }
  if (maintenanceTimer) {
    clearTimeout(maintenanceTimer);
  }
  window.removeEventListener("keydown", handleGlobalShortcut, true);
  window.removeEventListener("wheel", handleDashboardWheel, wheelOptions);
  window.removeEventListener("mousewheel", handleDashboardWheel, wheelOptions);
  document.removeEventListener("wheel", handleDashboardWheel, wheelOptions);
  document.removeEventListener("mousewheel", handleDashboardWheel, wheelOptions);
});

watch(activeTab, (tab) => {
  if (tab === "History") {
    loadHistory();
  }
});

const {
  newTasks,
  mainTasks,
  recordClipboard,
  expandedNew,
  focusTaskId,
  focusContentTarget,
  highlightTaskId,
  highlightNonce,
  mainCategories,
  loadDashboard,
  navigateToTask: navigateToDashboardTask,
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
  createTask,
  undoSignal,
  pollChanges,
  handleDayTick,
  initDayKey
} = useDashboardData({
  activeTab,
  dashboardColumnsRef,
  loadHistory,
  loadMaintenanceStatus,
  loadWarnings
});

const clearSourceNavigation = () => {
  searchReturnContext.value = null;
  searchNavigationTarget.value = null;
  historyNavigationTarget.value = null;
  peopleNavigationTarget.value = null;
};

const selectTab = (tab) => {
  if (taskClipboard.busy.value) return;
  if (startupSafety.value.recoveryMode && tab !== "Settings") {
    return;
  }
  clearSourceNavigation();
  activeTab.value = tab;
};

const openNewWindow = () => {
  if (!openNewGlanceWindow()) {
    window.alert("Glance could not open another window.");
  }
};


const openSearchSource = async (result) => {
  const destination = resolveSearchDestination(result?.task);
  if (!destination) {
    window.alert("This search result no longer has a view that Glance can open.");
    return;
  }

  const nonce = ++navigationNonce;
  searchReturnContext.value = { taskId: destination.taskId };
  searchNavigationTarget.value = null;
  historyNavigationTarget.value = null;
  peopleNavigationTarget.value = null;
  activeTab.value = destination.tab;
  await nextTick();

  try {
    if (destination.tab === "Dashboard") {
      const found = await navigateToDashboardTask(destination.taskId);
      if (!found) window.alert("The task is no longer visible on the Dashboard.");
      return;
    }
    if (destination.tab === "History") {
      await loadHistory();
      historyNavigationTarget.value = { taskId: destination.taskId, nonce };
      return;
    }
    peopleNavigationTarget.value = { ...destination, nonce };
  } catch (error) {
    window.alert(error instanceof Error ? error.message : "Glance could not open the search result.");
  }
};

const returnToSearch = async () => {
  const taskId = searchReturnContext.value?.taskId;
  historyNavigationTarget.value = null;
  peopleNavigationTarget.value = null;
  activeTab.value = "Search";
  await nextTick();
  searchNavigationTarget.value = taskId
    ? { taskId, nonce: ++navigationNonce }
    : null;
  searchReturnContext.value = null;
};

watch(focusTaskId, (id) => {
  if (!id) {
    return;
  }
  setTimeout(() => {
    if (focusTaskId.value === id) {
      focusTaskId.value = null;
    }
  }, 0);
});

watch(focusContentTarget, (target) => {
  if (!target) {
    return;
  }
  setTimeout(() => {
    if (focusContentTarget.value === target) {
      focusContentTarget.value = null;
    }
  }, 0);
});

const {
  dragOver,
  isDashboardDragging,
  startDrag,
  endDrag,
  dropOnTask,
  dropOnCategoryFromView,
  dropOnWeekdayFromView,
  setDragOver,
  clearDragOver,
  handleDashboardPointerDown,
  handleDashboardPointerMove,
  handleDashboardPointerUp
} = useDashboardDrag({
  activeTab,
  dashboardColumnsRef,
  getCategoryTasks,
  findTaskById,
  applyTaskMove
});

const dashboardClipboard = createTaskClipboardAdapter({
  tasks: () => [...newTasks.value, ...mainTasks.value], record: recordClipboard, refresh: loadDashboard
});
const dashboardHistory = async (operation) => {
  try { await operation(); } catch (error) { window.alert(error.message); }
};

const { handleDashboardWheel, handleGlobalShortcut } = useKeyboardShortcuts({
  activeTab,
  searchInputRef,
  dashboardColumnsRef,
  onOpenSearch: () => selectTab("Search"),
  onUndo: () => activeTab.value === "People" ? peopleViewRef.value?.undo?.() : dashboardHistory(undo),
  onRedo: () => activeTab.value === "People" ? peopleViewRef.value?.redo?.() : dashboardHistory(redo)
});

let pollTimer = null;
let dayTimer = null;
let maintenanceTimer = null;
const wheelOptions = { passive: false, capture: true };

const isLastTaskId = (task, list) => {
  if (!list || list.length === 0) {
    return false;
  }
  return list[list.length - 1]?.id === task.id;
};

const getTaskItemBindings = (task, list, options) => ({
  task,
  onSetCategory: setTaskCategory,
  showRecurrenceControls: options.showRecurrenceControls,
  onSetRecurrence: setTaskRecurrence,
  draggable: true,
  isLastInCategory: isLastTaskId(task, list),
  isDropTarget: dragOver.value.id === task.id,
  dropPosition: dragOver.value.position,
  categoryId: options.categoryId,
  dragCategoryId: options.dragCategoryId,
  onDragStart: startDrag,
  onDragEnd: endDrag,
  onDrop: dropOnTask,
  onDragOver: setDragOver,
  onDragLeave: clearDragOver,
  clipboardAdapter: dashboardClipboard,
  focusTitleId: focusTaskId.value,
  focusContentTarget: focusContentTarget.value,
  undoSignal: undoSignal.value,
  highlightId: highlightTaskId.value,
  highlightNonce: highlightNonce.value,
  onSave: saveTask,
  onComplete: toggleComplete,
  onDirty: handleDirtyChange,
  onCreateBelow: createTaskBelow,
  onSplitTitleToNewTask: splitTitleToNewTask,
  onTabToPrevious: moveTaskToPrevious,
  onMergeToPrevious: mergeTaskToPrevious,
  onSplitToNewTask: splitSubcontentToNewTask,
  onFocusPrevTaskFromTitle: focusPrevTaskFromTitle,
  onFocusNextTaskFromContent: focusNextTaskFromContent,
  onDelete: deleteTask,
  allowStatusMarkers: true,
  onStatusMarkers: updateStatusMarkers,
  sendPeople: peopleDirectory.value.people.filter((person) => !person.archivedAt),
  sendTags: peopleDirectory.value.tags,
  onSendToPeople: sendToPeople,
  onLoadSendEvents: loadTaskSendEvents,
  onDismissSendMarker: dismissSendMarker
});

const loadPeopleDirectory = async () => {
  try { peopleDirectory.value = await fetchPeople(false); }
  catch { peopleDirectory.value = { people: [], tags: [] }; }
};

const updateStatusMarkers = async (task, title, content) => {
  const response = await setStatusMarkers(task.id, { baseUpdatedAt: task.updatedAt, title, content });
  task.title = title;
  task.content = content;
  task.updatedAt = response.updatedAt;
  task.statusInputAt = response.statusInputAt;
  return response;
};

const sendToPeople = async (task, personIds, tagIds) => {
  const response = await sendTaskToPeople(task.id, personIds, tagIds);
  task.sendMarkerVisible = true;
  return response;
};
const loadTaskSendEvents = (task) => fetchTaskSendEvents(task.id);
const dismissSendMarker = async (task) => {
  const response = await dismissTaskSendMarker(task.id);
  task.sendMarkerVisible = false;
  return response;
};

const toggleExpandNew = () => {
  expandedNew.value = !expandedNew.value;
};

const createNewTaskFromEmpty = async (initialText = "") => {
  const title = initialText ? initialText : emptyTitleDoc();
  const newId = await createTask(
    DASHBOARD_NEW_PAGE,
    title,
    emptyContentDoc(),
    Date.now()
  );
  focusTaskId.value = newId;
};

const moveCompletedToHistory = async () => {
  await apiMoveCompletedToHistory();
  await loadDashboard();
  if (activeTab.value === "History") {
    await loadHistory();
  }
};
</script>

<style src="./styles/app.css"></style>
<style src="./styles/controls.css"></style>






