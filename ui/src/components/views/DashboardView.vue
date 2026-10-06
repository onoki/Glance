<template>
  <section class="dashboard" @scroll.capture="keepTaskListsLeft">
    <div
      ref="columnsRef"
      class="dashboard-columns"
      :class="{ 'maximized-column': maximizedCategoryId, 'dragging-dashboard': isDashboardDragging }"
      @pointerdown="handlePointerDown"
      @pointermove="handlePointerMove"
      @pointerup="handlePointerUp"
      @pointerleave="handlePointerUp"
    >
      <div
        class="dashboard-column new-column"
        :style="maximizedCategoryId === 'new' ? null : getColumnStyle('new')"
        :class="{ expanded: maximizedCategoryId === 'new', empty: newTasks.length === 0 && maximizedCategoryId !== 'new' }"
        data-category-id="new"
      >
        <section class="list-card" data-category-id="new">
          <header class="list-header column-header">
            <div>
              <h3 class="category-title">New tasks</h3>
            </div>
            <div class="header-actions">
              <ColumnViewButton label="New tasks" :maximized="maximizedCategoryId === 'new'" @click="onToggleMaximize('new')" />
            </div>
          </header>

          <div class="task-list" @dragover.prevent @drop.prevent="onDropOnCategory('new', $event)">
            <button
              v-if="newTasks.length === 0"
              type="button"
              class="empty-new"
              @click="props.onCreateNewTask()"
              @keydown="handleEmptyKeydown"
            >
              ...
            </button>
            <TransitionGroup name="task-move" tag="div" class="task-list-group">
              <TaskItem
                v-for="task in newTasks"
                :key="task.id"
                v-bind="getTaskItemBindings(task, newTasks, {
                  categoryId: 'new',
                  dragCategoryId: 'new',
                  showRecurrenceControls: false
                })"
              />
            </TransitionGroup>
            <button class="task-list-tail" type="button" aria-label="Add task to New tasks" @click="appendTask(newTasks, 'new')"><span>+ Add task</span></button>
          </div>
        </section>
        <div v-if="!maximizedCategoryId" class="column-resizer" @pointerdown="startResize('new', $event)"></div>
      </div>

      <template v-for="category in mainCategories" :key="category.id">
        <div
          v-if="category.tasks.length || isThisWeekCategory(category)"
          class="dashboard-column"
          :style="maximizedCategoryId === category.id ? null : getColumnStyle(category.id)"
          :class="{ expanded: maximizedCategoryId === category.id }"
          :data-category-id="category.id"
          @dragover.prevent
          @drop.prevent="onDropOnCategory(category.id, $event)"
        >
          <section class="list-card" :data-category-id="category.id">
            <header class="column-header">
              <h3 class="category-title">{{ category.label }}</h3>
              <ColumnViewButton :label="category.label" :maximized="maximizedCategoryId === category.id" @click="onToggleMaximize(category.id)" />
            </header>

            <div class="task-list">
              <template v-if="isThisWeekCategory(category)">
                <div
                  v-for="group in groupTasksByWeekday(category.tasks)"
                  :key="group.id"
                  class="weekday-group"
                  @dragover.prevent
                  @drop.prevent.stop="handleDropOnWeekday(category.id, group, $event)"
                >
                  <div class="weekday-header">
                    <span>{{ group.label }}</span>
                    <button
v-if="!group.isPast" type="button" class="weekday-add"
                      :aria-label="`Add task on ${group.label} ${group.dateKey}`"
                      :title="`Add task on ${group.dateKey}`" @click="appendDay(group)"
>
+
</button>
                  </div>
                  <TransitionGroup name="task-move" tag="div" class="task-list-group">
                    <TaskItem
                      v-for="task in group.tasks"
                      :key="task.id"
                      v-bind="getTaskItemBindings(task, category.tasks, {
                        categoryId: category.id,
                        dragCategoryId: category.id,
                        showRecurrenceControls: category.id === 'repeatable'
                      })"
                    />
                  </TransitionGroup>
                </div>
              </template>
              <template v-else>
                <TransitionGroup name="task-move" tag="div" class="task-list-group">
                  <TaskItem
                    v-for="task in category.tasks"
                    :key="task.id"
                    v-bind="getTaskItemBindings(task, category.tasks, {
                      categoryId: category.id,
                      dragCategoryId: category.id,
                      showRecurrenceControls: category.id === 'repeatable'
                    })"
                  />
                </TransitionGroup>
              </template>
              <button class="task-list-tail" type="button" :aria-label="`Add task to ${category.label}`" @click="appendTask(category.tasks, category.id)"><span>+ Add task</span></button>
            </div>
          </section>
          <div
            v-if="!maximizedCategoryId"
            class="column-resizer"
            @pointerdown="startResize(category.id, $event)"
          ></div>
        </div>
      </template>
    </div>
  </section>
</template>

<script setup>
import { nextTick, onBeforeUnmount, ref, watch } from "vue";
import TaskItem from "../TaskItem.vue";
import ColumnViewButton from "../ColumnViewButton.vue";
import { keepTaskListsLeft } from "../../utils/taskListScroll.js";

const props = defineProps({
  newTasks: {
    type: Array,
    required: true
  },
  mainCategories: {
    type: Array,
    required: true
  },
  maximizedCategoryId: {
    type: String,
    default: null
  },
  isDashboardDragging: {
    type: Boolean,
    required: true
  },
  dashboardColumnsRef: {
    type: Object,
    default: null
  },
  getTaskItemBindings: {
    type: Function,
    required: true
  },
  groupTasksByWeekday: {
    type: Function,
    required: true
  },
  isThisWeekCategory: {
    type: Function,
    required: true
  },
  onMoveNewToMain: {
    type: Function,
    required: true
  },
  onToggleMaximize: {
    type: Function,
    required: true
  },
  onDropOnCategory: {
    type: Function,
    required: true
  },
  onPointerDown: {
    type: Function,
    required: true
  },
  onPointerMove: {
    type: Function,
    required: true
  },
  onPointerUp: {
    type: Function,
    required: true
  },
  onCreateNewTask: {
    type: Function,
    required: true
  },
  onCreateDayTask: { type: Function, required: true },
  onDropOnWeekday: {
    type: Function,
    required: true
  }
});

const emit = defineEmits(["update:dashboardColumnsRef"]);
const columnsRef = ref(null);
let appending = false;
const appendTask = async (tasks, categoryId) => {
  if (appending) return;
  appending = true;
  try {
    const last = tasks[tasks.length - 1];
    if (!last && categoryId !== "new") await props.onCreateDayTask(new Date());
    else if (!last) await props.onCreateNewTask();
    else await props.getTaskItemBindings(last, tasks, { categoryId }).onCreateBelow(last, categoryId, null, { inheritScheduledDate: false });
  } finally { appending = false; }
};
const resizing = ref(null);
const MIN_COLUMN_WIDTH = 200;
const viewportKey = 'glance:dashboard-scroll-before-maximize';
let normalScrollLeft = (() => {
  try {
    const saved = Number(sessionStorage.getItem(viewportKey));
    return Number.isFinite(saved) ? Math.max(0, saved) : 0;
  }
  catch { return 0; }
})();
watch(() => props.maximizedCategoryId, async (id, previous) => {
  if (id && !previous) {
    normalScrollLeft = columnsRef.value?.scrollLeft || 0;
    try { sessionStorage.setItem(viewportKey, String(normalScrollLeft)); }
    catch { /* Keep the in-memory viewport when storage is unavailable. */ }
  }
  await nextTick();
  if (columnsRef.value) columnsRef.value.scrollLeft = id ? 0 : normalScrollLeft;
});
watch(() => props.mainCategories, categories => {
  const id = props.maximizedCategoryId;
  if (id && id !== 'new' && !categories.some(category => category.id === id && (category.tasks.length || props.isThisWeekCategory(category)))) {
    props.onToggleMaximize(id);
  }
}, { immediate: true });

watch(columnsRef, (value) => {
  emit("update:dashboardColumnsRef", value);
});

onBeforeUnmount(() => {
  emit("update:dashboardColumnsRef", null);
});

const handleEmptyKeydown = (event) => {
  if (!event) {
    return;
  }
  if (event.key === "Enter") {
    event.preventDefault();
    props.onCreateNewTask();
    return;
  }
  if (event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
    event.preventDefault();
    props.onCreateNewTask(event.key);
  }
};

const appendDay = async (group) => {
  if (appending || group.isPast) return;
  appending = true;
  try { await props.onCreateDayTask(group.dateKey); }
  finally { appending = false; }
};

const handleDropOnWeekday = (categoryId, group, event) => {
  if (!group.isPast) return props.onDropOnWeekday(categoryId, group.dateKey, event);
};

const loadColumnWidths = () => {
  try {
    const raw = localStorage.getItem("glance:column-widths");
    if (!raw) {
      return {};
    }
    const parsed = JSON.parse(raw);
    return parsed && typeof parsed === "object" ? parsed : {};
  } catch {
    return {};
  }
};

const saveColumnWidths = () => {
  try {
    localStorage.setItem("glance:column-widths", JSON.stringify(columnWidths.value));
  } catch {
    // ignore storage failures
  }
};

const columnWidths = ref(loadColumnWidths());

const getColumnStyle = (categoryId) => {
  const width = columnWidths.value?.[categoryId];
  if (!width) {
    return null;
  }
  const resolved = Math.max(MIN_COLUMN_WIDTH, width);
  return {
    width: `${resolved}px`,
    minWidth: `${resolved}px`,
    maxWidth: `${resolved}px`
  };
};

const startResize = (categoryId, event) => {
  if (!event || event.button !== 0) {
    return;
  }
  event.preventDefault();
  event.stopPropagation();
  const column = event.target?.closest?.(".dashboard-column");
  const measuredWidth = column ? column.getBoundingClientRect().width : null;
  const startWidth = measuredWidth || columnWidths.value?.[categoryId] || MIN_COLUMN_WIDTH;
  resizing.value = {
    id: categoryId,
    startX: event.clientX,
    startWidth,
    pointerId: event.pointerId
  };
  event.target?.setPointerCapture?.(event.pointerId);
};

const handlePointerDown = (event) => {
  if (event?.target?.closest?.(".column-resizer")) {
    return;
  }
  props.onPointerDown(event);
};

const handlePointerMove = (event) => {
  if (resizing.value) {
    event.preventDefault();
    const delta = event.clientX - resizing.value.startX;
    const nextWidth = Math.max(MIN_COLUMN_WIDTH, Math.round(resizing.value.startWidth + delta));
    columnWidths.value = { ...columnWidths.value, [resizing.value.id]: nextWidth };
    return;
  }
  props.onPointerMove(event);
};

const handlePointerUp = (event) => {
  if (resizing.value) {
    event?.target?.releasePointerCapture?.(resizing.value.pointerId);
    resizing.value = null;
    saveColumnWidths();
    return;
  }
  props.onPointerUp(event);
};
</script>
