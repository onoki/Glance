<template>
  <div class="task-actions-area" @click.stop>
    <button
      v-if="draggable && !readOnly"
      type="button"
      class="drag-handle"
      aria-label="Drag task"
      :draggable="true"
      @dragstart.stop="emitDragStart"
    >
      ::
    </button>
    <div v-if="showCategoryActions" class="task-actions">
      <button type="button" class="task-action" @click="emitSetCategory('uncategorized')">Uncategorized</button>
      <button type="button" class="task-action" @click="emitSetCategory('this-week')">This week</button>
      <button type="button" class="task-action" @click="emitSetCategory('next-week')">Next week</button>
      <button type="button" class="task-action" @click="emitSetCategory('no-date')">No date</button>
      <button type="button" class="task-action" @click="emitSetCategory('repeatable')">Repeatable</button>
      <button type="button" class="task-action" @click="emitSetCategory('notes')">Notes</button>
    </div>
  </div>
</template>

<script setup>
const props = defineProps({
  showCategoryActions: {
    type: Boolean,
    default: false
  },
  draggable: {
    type: Boolean,
    default: false
  },
  readOnly: {
    type: Boolean,
    default: false
  }
});

const emit = defineEmits(["set-category", "drag-start"]);

const emitSetCategory = (category) => {
  emit("set-category", category);
};

const emitDragStart = (event) => {
  emit("drag-start", event);
};
</script>

<style scoped>
.task-actions-area {
  position: absolute;
  top: -2px;
  right: 14px;
  display: flex;
  gap: 6px;
  opacity: 0;
  pointer-events: none;
  flex-wrap: wrap;
  justify-content: flex-end;
  z-index: 2;
}

:deep(.task-item:hover) .task-actions-area {
  opacity: 1;
}

.task-actions {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
  justify-content: flex-end;
}

.task-actions-area .task-action,
.task-actions-area .drag-handle {
  pointer-events: auto;
}

.task-action {
  border: 1px solid var(--border-panel);
  background: var(--bg-panel);
  color: var(--text-main);
  padding: 2px 6px;
  border-radius: 0;
  font-size: 0.7rem;
  cursor: pointer;
  pointer-events: auto;
}

.drag-handle {
  border: 1px solid var(--border-panel);
  background: var(--bg-panel);
  color: var(--text-muted);
  border-radius: 0;
  padding: 0 6px;
  font-size: 0.8rem;
  line-height: 1.2rem;
  cursor: grab;
  user-select: none;
}

.drag-handle:active {
  cursor: grabbing;
}
</style>
