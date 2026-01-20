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
              <p class="subtitle">Fresh captures that still need a home.</p>
            </div>
            <div class="header-actions">
              <button class="ghost" @click="onMoveNewToMain" :disabled="newTasks.length === 0">
                Move to Uncategorized
              </button>
              <button class="ghost" @click="onToggleExpand">
                {{ expandedNew ? "Restore view" : "Expand" }}
              </button>
            </div>
          </header>

          <div class="task-list" @dragover.prevent @drop.prevent="onDropOnCategory('new', $event)">
            <TaskItem
              v-for="task in newTasks"
              :key="task.id"
              v-bind="getTaskItemBindings(task, newTasks, {
                categoryId: 'new',
                dragCategoryId: 'new',
                showCategoryActions: false,
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
                <div v-for="group in groupTasksByWeekday(category.tasks)" :key="group.id" class="weekday-group">
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

defineProps({
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
</script>
