<template>
  <div class="task-item" :class="{ completed: !!task.completedAt }">
    <label class="task-check">
      <input type="checkbox" :checked="!!task.completedAt" @change="toggleComplete" />
      <span></span>
    </label>
    <div class="task-body">
      <div class="title-row">
        <input
          ref="titleInput"
          class="task-title"
          v-model="title"
          @input="scheduleSave"
          @blur="handleTitleBlur"
          @keydown="handleTitleKeydown"
          placeholder="Task title"
        />
        <span v-if="dirty" class="dirty-badge">dirty</span>
      </div>
      <RichTextEditor
        ref="editorRef"
        v-model="content"
        :on-dirty="handleEditorDirty"
      />
    </div>
  </div>
</template>

<script setup>
import { ref, watch } from "vue";
import RichTextEditor from "./RichTextEditor.vue";

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
  },
  onCreateBelow: {
    type: Function,
    required: true
  }
});

const title = ref(props.task.title);
const content = ref(props.task.content);
const dirty = ref(false);
const editorRef = ref(null);
const titleInput = ref(null);
let saveTimer = null;
let pendingCreateTimer = null;

const snapshot = () => ({
  ...props.task,
  title: title.value,
  content: content.value
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
  saveTimer = setTimeout(saveNow, 800);
};

const saveNow = async () => {
  if (!dirty.value) {
    return;
  }
  if (saveTimer) {
    clearTimeout(saveTimer);
    saveTimer = null;
  }
  const trimmed = title.value.trim();
  if (!trimmed) {
    title.value = "Untitled";
  }
  try {
    await props.onSave({
      id: props.task.id,
      title: title.value,
      content: content.value,
      baseUpdatedAt: props.task.updatedAt,
      page: props.task.page
    });
    dirty.value = false;
    props.onDirty(props.task.id, false, null);
  } catch {
    // keep dirty so user can retry
  }
};

const handleEditorDirty = () => {
  scheduleSave();
};

const handleTitleBlur = () => {
  saveNow();
};

const handleTitleKeydown = (event) => {
  if (event.key === "Tab") {
    event.preventDefault();
    if (pendingCreateTimer) {
      clearTimeout(pendingCreateTimer);
      pendingCreateTimer = null;
    }
    editorRef.value?.insertParagraphIfEmpty();
    return;
  }

  if (event.key !== "Enter" && event.key !== "NumpadEnter") {
    return;
  }

  event.preventDefault();
  const input = titleInput.value;
  if (!input) {
    return;
  }
  if (input.selectionStart !== input.value.length) {
    return;
  }

  saveNow();

  if (pendingCreateTimer) {
    clearTimeout(pendingCreateTimer);
  }

  pendingCreateTimer = setTimeout(() => {
    pendingCreateTimer = null;
    props.onCreateBelow(props.task);
  }, 250);
};

const toggleComplete = () => {
  props.onComplete(props.task);
};

watch(
  () => props.task,
  (task) => {
    if (!task || dirty.value) {
      return;
    }
    title.value = task.title;
    content.value = task.content;
  },
  { deep: true }
);
</script>

<style scoped>
.task-item {
  display: grid;
  grid-template-columns: 28px 1fr;
  gap: 12px;
  padding: 12px;
  border-radius: 16px;
  background: #fef7ed;
  border: 1px solid #efe1d0;
}

.task-item.completed {
  color: #8a8177;
  text-decoration: line-through;
}

.task-check {
  display: grid;
  place-items: center;
}

.task-check input {
  display: none;
}

.task-check span {
  width: 18px;
  height: 18px;
  border: 2px solid #6f665f;
  border-radius: 6px;
  display: inline-block;
  position: relative;
}

.task-check input:checked + span::after {
  content: "";
  position: absolute;
  inset: 3px;
  background: #6f665f;
  border-radius: 3px;
}

.task-body {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.title-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.task-title {
  border: none;
  background: transparent;
  font-weight: 600;
  font-size: 1rem;
  padding: 0;
  outline: none;
  flex: 1;
}

.dirty-badge {
  font-size: 0.7rem;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  padding: 2px 6px;
  border-radius: 999px;
  background: #f6d7a7;
  color: #5a3f1b;
}
</style>
