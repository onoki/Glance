<template>
  <section class="dashboard">
    <div
      ref="columnsRef"
      class="dashboard-columns"
      :class="{ 'expanded-new': expandedNew, 'dragging-dashboard': isDashboardDragging }"
      @pointerdown="onPointerDown"
      @pointermove="onPointerMove"
      @pointerup="onPointerUp"
      @pointerleave="onPointerUp"
    >
      <div class="dashboard-column new-column" :class="{ expanded: expandedNew }">
        <section class="list-card">
          <header class="list-header column-header">
            <div>
              <h2>New tasks</h2>
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
                showCategoryActions: true,
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
                      showCategoryActions: true,
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
                    showCategoryActions: true,
                    showRecurrenceControls: category.id === 'repeatable'
                  })"
                />
              </template>
            </div>
          </section>
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
</script>
