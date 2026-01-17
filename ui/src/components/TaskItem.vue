<template>
  <div class="task-item" :class="{ completed: !!task.completedAt }">
    <label class="task-check">
      <input type="checkbox" :checked="!!task.completedAt" @change="toggleComplete" />
      <span></span>
    </label>
    <div class="task-body">
      <input
        class="task-title"
        v-model="title"
        @input="scheduleSave"
        @blur="saveNow"
        placeholder="Task title"
      />
      <textarea
        class="task-content"
        v-model="contentText"
        @input="scheduleSave"
        @blur="saveNow"
        rows="2"
        placeholder="Add details"
      ></textarea>
    </div>
  </div>
</template>

<script setup>
import { ref, watch } from "vue";

const props = defineProps({
  task: {
    type: Object,
    required: true
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
  }
});

const title = ref(props.task.title);
const contentText = ref(props.task.contentText || "");
const dirty = ref(false);
let saveTimer = null;

const snapshot = () => ({
  ...props.task,
  title: title.value,
  contentText: contentText.value
});

const markDirty = () => {
  if (!dirty.value) {
    dirty.value = true;
  }
  props.onDirty(props.task.id, true, snapshot());
};

const scheduleSave = () => {
  markDirty();
  if (saveTimer) {
    clearTimeout(saveTimer);
  }
  saveTimer = setTimeout(saveNow, 700);
};

const saveNow = async () => {
  if (!dirty.value) {
    return;
  }
  if (saveTimer) {
    clearTimeout(saveTimer);
    saveTimer = null;
  }
  try {
    await props.onSave({
      id: props.task.id,
      title: title.value,
      contentText: contentText.value,
      baseUpdatedAt: props.task.updatedAt,
      page: props.task.page
    });
    dirty.value = false;
    props.onDirty(props.task.id, false, null);
  } catch {
    // keep dirty state so user can retry
  }
};

const toggleComplete = () => {
  props.onComplete(props.task);
};

watch(
  () => props.task,
  (task) => {
    if (dirty.value || !task) {
      return;
    }
    title.value = task.title;
    contentText.value = task.contentText || "";
  },
  { deep: true }
);
</script>
