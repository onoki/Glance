<template>
  <section class="search-view">
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
        <TaskItem
          v-for="result in searchResults"
          :key="result.task.id"
          :task="result.task"
          :read-only="true"
          :allow-toggle="false"
          :focus-title-id="null"
          :focus-content-target="null"
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
  </section>
</template>

<script setup>
import TaskItem from "../TaskItem.vue";

defineProps({
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
  }
});

defineEmits(["update:searchQuery"]);
</script>
