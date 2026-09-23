<template>
  <section ref="searchRoot" class="search-view">
    <div class="search-bar">
      <input
        :value="searchQuery"
        type="search"
        placeholder="Search tasks"
        @input="$emit('update:searchQuery', $event.target.value)"
        @keydown.enter.prevent="onSearchTasks"
        :ref="searchInputRef"
      />
      <button class="add-task" :disabled="isSearching" @click="onSearchTasks">Search</button>
    </div>
    <div class="search-results">
      <div v-if="!hasSearched" class="search-empty"></div>
      <div v-else-if="searchResults.length === 0" class="search-empty">No results</div>
      <div v-else class="search-list">
        <div v-for="result in searchResults" :key="result.task.id" class="search-result">
          <div class="search-result-actions">
            <span class="search-result-context">{{ describeSource(result.task) }}</span>
            <button type="button" class="ghost search-open-source" @click="onOpenSource(result)" @keydown.down.prevent="focusResult(result.task.id, 1)" @keydown.up.prevent="focusResult(result.task.id, -1)">Open source</button>
          </div>
          <TaskItem
            :task="result.task"
            :read-only="true"
            :allow-toggle="false"
            :allow-delete="false"
            :focus-title-id="null"
            :focus-content-target="null"
            :highlight-id="navigationTarget?.taskId || null"
            :highlight-nonce="navigationTarget?.nonce || 0"
            :on-save="noop"
            :on-complete="noop"
            :on-dirty="noop"
            :on-create-below="noop"
            :on-tab-to-previous="noopAsync"
            :on-split-to-new-task="noop"
            :on-focus-prev-task-from-title="noop"
            :on-focus-next-task-from-content="noop"
            :on-delete="noop"
          />
        </div>
      </div>
    </div>
  </section>
</template>

<script setup>
import { nextTick, ref, watch } from "vue";
import TaskItem from "../TaskItem.vue";

const props = defineProps({
  searchQuery: {
    type: String,
    required: true
  },
  searchResults: {
    type: Array,
    required: true
  },
  hasSearched: {
    type: Boolean,
    required: true
  },
  isSearching: {
    type: Boolean,
    required: true
  },
  onSearchTasks: {
    type: Function,
    required: true
  },
  searchInputRef: {
    type: Object,
    default: null
  },
  noop: {
    type: Function,
    required: true
  },
  noopAsync: {
    type: Function,
    required: true
  },
  onOpenSource: {
    type: Function,
    required: true
  },
  navigationTarget: {
    type: Object,
    default: null
  }
});

defineEmits(["update:searchQuery"]);

const searchRoot = ref(null);
const focusResult = (id, step) => {
  const index = props.searchResults.findIndex(result => result.task.id === id);
  const buttons = searchRoot.value?.querySelectorAll('.search-open-source');
  buttons?.[Math.max(0, Math.min(buttons.length - 1, index + step))]?.focus();
};

const describeSource = (task) => {
  if (task.completedAt !== null && task.completedAt !== undefined) {
    return "History";
  }
  if (task.page === "people:main") {
    return task.ownerPersonName ? `People · ${task.ownerPersonName}` : "People";
  }
  const category = task.page === 'dashboard:new' ? 'New tasks' : task.recurrence?.type === 'notes'
    ? 'Notes' : task.recurrence ? 'Repeatable' : task.scheduledDate === 'no-date'
      ? 'No date' : task.scheduledDate || 'Uncategorized';
  return `Dashboard · ${category}`;
};

const scrollToNavigationTarget = async (target) => {
  if (!target?.taskId) return;
  await nextTick();
  requestAnimationFrame(() => {
    const element = Array.from(searchRoot.value?.querySelectorAll?.("[data-task-id]") || [])
      .find((candidate) => candidate.dataset.taskId === target.taskId);
    element?.scrollIntoView?.({ block: "center", inline: "nearest", behavior: "smooth" });
  });
};

watch(() => props.navigationTarget, scrollToNavigationTarget, { immediate: true });
</script>
