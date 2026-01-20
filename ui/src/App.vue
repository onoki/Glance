<template>
  <div class="app-shell">
    <header class="top-nav">
      <div class="brand">Glance</div>
      <nav class="tabs">
        <button
          v-for="tab in tabs"
          :key="tab"
          class="tab"
          :class="{ active: activeTab === tab }"
          @click="activeTab = tab"
        >
          {{ tab }}
        </button>
      </nav>
    </header>

    <main class="content">
      <div v-if="visibleWarnings.length" class="warning-banner">
        <div v-for="warning in visibleWarnings" :key="warning.id" class="warning-item">
          <span>{{ warning.message }}</span>
          <button class="warning-dismiss" @click="dismissWarning(warning.id)">Dismiss</button>
        </div>
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
        :on-pointer-down="handleDashboardPointerDown"
        :on-pointer-move="handleDashboardPointerMove"
        :on-pointer-up="handleDashboardPointerUp"
      />

      <HistoryView
        v-else-if="activeTab === 'History'"
        :history-bars="historyBars"
        :history-scale="historyScale"
        :history-series="historySeries"
        :history-groups="historyGroups"
        :on-move-completed-to-history="moveCompletedToHistory"
        :on-complete="toggleComplete"
        :noop="noop"
        :noop-async="noopAsync"
      />

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
      />

      <SettingsView
        v-else-if="activeTab === 'Settings'"
        :is-backing-up="isBackingUp"
        :is-reindexing="isReindexing"
        :backup-status="backupStatus"
        :reindex-status="reindexStatus"
        :maintenance-status="maintenanceStatus"
        :app-version="appVersion"
        :on-backup-now="backupNow"
        :on-reindex-search="reindexSearch"
      />

      <section v-else class="placeholder">
        <h2>{{ activeTab }}</h2>
        <p>This section will be implemented in the next iteration.</p>
      </section>
    </main>
    <footer class="app-footer">Version {{ appVersion || "Unknown" }} UTC</footer>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from "vue";
import { moveCompletedToHistory as apiMoveCompletedToHistory } from "./api/tasks.js";
import DashboardView from "./components/views/DashboardView.vue";
import HistoryView from "./components/views/HistoryView.vue";
import SearchView from "./components/views/SearchView.vue";
import SettingsView from "./components/views/SettingsView.vue";
import { groupTasksByWeekday, isThisWeekCategory } from "./utils/categoryUtils.js";
import { useHistory } from "./composables/useHistory.js";
import { useMaintenance } from "./composables/useMaintenance.js";
import { useSearch } from "./composables/useSearch.js";
import { useDashboardData } from "./composables/useDashboardData.js";
import { useDashboardDrag } from "./composables/useDashboardDrag.js";
import { useKeyboardShortcuts } from "./composables/useKeyboardShortcuts.js";

const tabs = ["Dashboard", "History", "Search", "Settings"];
const activeTab = ref("Dashboard");

const dashboardColumnsRef = ref(null);

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
  loadMaintenanceStatus,
  loadVersion,
  loadWarnings,
  maintenanceStatus,
  reindexSearch,
  reindexStatus,
  visibleWarnings
} = useMaintenance();

const noop = () => {};
const noopAsync = async () => false;

onMounted(async () => {
  await loadDashboard();
  if (activeTab.value === "History") {
    await loadHistory();
  }
  await loadWarnings();
  await loadMaintenanceStatus();
  await loadVersion();
  maintenanceTimer = setTimeout(() => {
    loadMaintenanceStatus();
  }, 2500);
  initDayKey();
  pollTimer = setInterval(pollChanges, 750);
  dayTimer = setInterval(handleDayTick, 60000);

  window.addEventListener("keydown", handleGlobalShortcut);
  window.addEventListener("wheel", handleDashboardWheel, { passive: false });
});

onBeforeUnmount(() => {
  if (pollTimer) {
    clearInterval(pollTimer);
  }
  if (dayTimer) {
    clearInterval(dayTimer);
  }
  if (maintenanceTimer) {
    clearTimeout(maintenanceTimer);
  }
  window.removeEventListener("keydown", handleGlobalShortcut);
  window.removeEventListener("wheel", handleDashboardWheel);
});

watch(activeTab, (tab) => {
  if (tab === "History") {
    loadHistory();
  }
});

const {
  newTasks,
  mainTasks,
  expandedNew,
  focusTaskId,
  focusContentTarget,
  mainCategories,
  loadDashboard,
  createTaskBelow,
  saveTask,
  deleteTask,
  toggleComplete,
  moveNewToMain,
  setTaskCategory,
  setTaskRecurrence,
  splitSubcontentToNewTask,
  moveTaskToPrevious,
  focusPrevTaskFromTitle,
  focusNextTaskFromContent,
  handleDirtyChange,
  applyTaskMove,
  findTaskById,
  getCategoryTasks,
  pollChanges,
  handleDayTick,
  initDayKey
} = useDashboardData({
  activeTab,
  loadHistory,
  loadMaintenanceStatus,
  loadWarnings
});

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

const { handleDashboardWheel, handleGlobalShortcut } = useKeyboardShortcuts({
  activeTab,
  searchInputRef,
  dashboardColumnsRef
});

let pollTimer = null;
let dayTimer = null;
let maintenanceTimer = null;

const isLastTaskId = (task, list) => {
  if (!list || list.length === 0) {
    return false;
  }
  return list[list.length - 1]?.id === task.id;
};

const getTaskItemBindings = (task, list, options) => ({
  task,
  showCategoryActions: options.showCategoryActions,
  onSetCategory: options.showCategoryActions ? setTaskCategory : null,
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
  focusTitleId: focusTaskId.value,
  focusContentTarget: focusContentTarget.value,
  onSave: saveTask,
  onComplete: toggleComplete,
  onDirty: handleDirtyChange,
  onCreateBelow: createTaskBelow,
  onTabToPrevious: moveTaskToPrevious,
  onSplitToNewTask: splitSubcontentToNewTask,
  onFocusPrevTaskFromTitle: focusPrevTaskFromTitle,
  onFocusNextTaskFromContent: focusNextTaskFromContent,
  onDelete: deleteTask
});

const toggleExpandNew = () => {
  expandedNew.value = !expandedNew.value;
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








