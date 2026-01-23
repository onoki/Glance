<template>
  <section class="dashboard">
    <div
      ref="columnsRef"
      class="dashboard-columns"
      :class="{ 'expanded-new': expandedNew, 'dragging-dashboard': isDashboardDragging }"
      @pointerdown="handlePointerDown"
      @pointermove="handlePointerMove"
      @pointerup="handlePointerUp"
      @pointerleave="handlePointerUp"
    >
      <div
        class="dashboard-column new-column"
        :class="{ expanded: expandedNew, empty: newTasks.length === 0 && !expandedNew }"
      >
        <section class="list-card">
          <header class="list-header column-header">
            <div>
              <h3 class="category-title">New tasks</h3>
            </div>
            <div class="header-actions">
              <button class="ghost" @click="onToggleExpand">
                {{ expandedNew ? "Restore view" : "Expand" }}
              </button>
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
            <TaskItem
              v-for="task in newTasks"
              :key="task.id"
              v-bind="getTaskItemBindings(task, newTasks, {
                categoryId: 'new',
                dragCategoryId: 'new',
                showRecurrenceControls: false
              })"
            />
          </div>
        </section>
      </div>

      <template v-for="category in mainCategories" :key="category.id">
        <div
          v-if="category.tasks.length"
          class="dashboard-column"
          :style="getColumnStyle(category.id)"
          @dragover.prevent
          @drop.prevent="onDropOnCategory(category.id, $event)"
        >
          <section class="list-card">
            <header class="column-header">
              <h3 class="category-title">{{ category.label }}</h3>
            </header>

            <div class="task-list">
              <template v-if="isThisWeekCategory(category)">
                <div
                  v-for="group in groupTasksByWeekday(category.tasks)"
                  :key="group.id"
                  class="weekday-group"
                  @dragover.prevent
                  @drop.prevent="handleDropOnWeekday(category.id, group.id, $event)"
                >
                  <div class="weekday-header">{{ group.label }}</div>
                  <TaskItem
                    v-for="task in group.tasks"
                    :key="task.id"
                    v-bind="getTaskItemBindings(task, category.tasks, {
                      categoryId: category.id,
                      dragCategoryId: category.id,
                      showRecurrenceControls: category.id === 'repeatable'
                    })"
                  />
                </div>
              </template>
              <template v-else>
                <TaskItem
                v-for="task in category.tasks"
                :key="task.id"
                v-bind="getTaskItemBindings(task, category.tasks, {
                  categoryId: category.id,
                  dragCategoryId: category.id,
                  showRecurrenceControls: category.id === 'repeatable'
                })"
              />
              </template>
            </div>
          </section>
          <div
            class="column-resizer"
            @pointerdown="startResize(category.id, $event)"
          ></div>
        </div>
      </template>
    </div>
  </section>
</template>

<script setup>
import { onBeforeUnmount, ref, watch } from "vue";
import TaskItem from "../TaskItem.vue";

const props = defineProps({
  newTasks: {
    type: Array,
    required: true
  },
  mainCategories: {
    type: Array,
    required: true
  },
  expandedNew: {
    type: Boolean,
    required: true
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
  onToggleExpand: {
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
  onDropOnWeekday: {
    type: Function,
    required: true
  }
});

const emit = defineEmits(["update:dashboardColumnsRef"]);
const columnsRef = ref(null);
const resizing = ref(null);
const MIN_COLUMN_WIDTH = 200;

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

const handleDropOnWeekday = (categoryId, weekdayId, event) => {
  const match = typeof weekdayId === "string" ? weekdayId.match(/weekday-(\d+)/) : null;
  const dayIndex = match ? Number.parseInt(match[1], 10) : 1;
  const now = new Date();
  const weekStart = new Date(now);
  const day = weekStart.getDay();
  const diff = (day + 6) % 7;
  weekStart.setDate(weekStart.getDate() - diff + (dayIndex - 1));
  weekStart.setHours(0, 0, 0, 0);
  const dateKey = `${weekStart.getFullYear()}-${`${weekStart.getMonth() + 1}`.padStart(2, "0")}-${`${weekStart.getDate()}`.padStart(2, "0")}`;
  props.onDropOnWeekday(categoryId, dateKey, event);
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
