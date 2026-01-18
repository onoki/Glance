<template>
  <div class="task-item" :class="{ completed: !!task.completedAt }">
    <label class="task-check">
      <input type="checkbox" :checked="!!task.completedAt" :disabled="readOnly" @change="toggleComplete" />
      <span></span>
    </label>
    <div class="task-body">
      <div class="title-row">
        <RichTextEditor
          ref="titleEditorRef"
          class="title-editor"
          v-model="title"
          mode="title"
          :editable="!readOnly"
          :on-dirty="handleTitleDirty"
          :on-split-to-new-task="noopSplit"
          :on-key-down="handleTitleKeydown"
        />
        <span v-if="dirty && !readOnly" class="dirty-badge">dirty</span>
      </div>
      <RichTextEditor
        ref="contentEditorRef"
        v-model="content"
        :editable="!readOnly"
        :on-dirty="handleContentDirty"
        :on-split-to-new-task="handleSplitToNewTask"
        :on-key-down="handleContentKeydown"
      />
    </div>
  </div>
</template>

<script setup>
import { nextTick, ref, watch } from "vue";
import RichTextEditor from "./RichTextEditor.vue";

const props = defineProps({
  task: {
    type: Object,
    required: true
  },
  readOnly: {
    type: Boolean,
    default: false
  },
  focusTitleId: {
    type: String,
    default: null
  },
  focusContentTarget: {
    type: Object,
    default: null
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
  },
  onTabToPrevious: {
    type: Function,
    required: true
  },
  onSplitToNewTask: {
    type: Function,
    required: true
  },
  onFocusPrevTaskFromTitle: {
    type: Function,
    required: true
  },
  onFocusNextTaskFromContent: {
    type: Function,
    required: true
  }
});

const title = ref(props.task.title);
const content = ref(props.task.content);
const dirty = ref(false);
const titleEditorRef = ref(null);
const contentEditorRef = ref(null);
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
  if (props.readOnly) {
    return;
  }
  markDirty();
  if (saveTimer) {
    clearTimeout(saveTimer);
  }
  saveTimer = setTimeout(saveNow, 800);
};

const saveNow = async () => {
  if (props.readOnly) {
    return;
  }
  if (!dirty.value) {
    return;
  }
  if (saveTimer) {
    clearTimeout(saveTimer);
    saveTimer = null;
  }
  if (!title.value || !title.value.content || title.value.content.length === 0) {
    title.value = {
      type: "doc",
      content: [{ type: "paragraph" }]
    };
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

const handleTitleDirty = () => {
  if (props.readOnly) {
    return;
  }
  scheduleSave();
};

const handleContentDirty = () => {
  if (props.readOnly) {
    return;
  }
  scheduleSave();
};

const isSelectionAtEnd = (editor) => {
  if (!editor) {
    return false;
  }
  const { selection } = editor.state;
  if (!selection.empty) {
    return false;
  }
  return selection.$from.pos === selection.$from.end();
};

const isSelectionAtStart = (editor) => {
  if (!editor) {
    return false;
  }
  const { selection } = editor.state;
  if (!selection.empty) {
    return false;
  }
  return selection.$from.pos === selection.$from.start();
};

const handleTitleKeydown = (event, editor) => {
  if (props.readOnly) {
    return false;
  }
  if (event.key === "Tab") {
    if (pendingCreateTimer) {
      clearTimeout(pendingCreateTimer);
      pendingCreateTimer = null;
    }
    saveNow();
    props.onTabToPrevious(props.task).then((moved) => {
      if (!moved) {
        contentEditorRef.value?.insertParagraphIfEmpty();
      }
    });
    return true;
  }

  if (event.key === "ArrowDown") {
    if (!isSelectionAtEnd(editor)) {
      return false;
    }
    event.preventDefault();
    contentEditorRef.value?.focusListItem(0, "start");
    return true;
  }

  if (event.key === "ArrowUp") {
    if (!isSelectionAtStart(editor)) {
      return false;
    }
    event.preventDefault();
    props.onFocusPrevTaskFromTitle(props.task);
    return true;
  }

  if (event.key === "Enter" && !event.shiftKey) {
    if (pendingCreateTimer) {
      clearTimeout(pendingCreateTimer);
    }
    saveNow();
    pendingCreateTimer = setTimeout(() => {
      pendingCreateTimer = null;
      props.onCreateBelow(props.task);
    }, 250);
    return true;
  }

  return false;
};

const getListContext = (editor) => {
  if (!editor) {
    return null;
  }
  const { $from, empty } = editor.state.selection;
  if (!empty) {
    return null;
  }
  let listItemDepth = null;
  for (let depth = $from.depth; depth > 0; depth -= 1) {
    if ($from.node(depth).type.name === "listItem") {
      listItemDepth = depth;
      break;
    }
  }
  if (!listItemDepth) {
    return null;
  }
  const listDepth = listItemDepth - 1;
  const listNode = $from.node(listDepth);
  if (!listNode || listNode.type.name !== "bulletList" || listDepth !== 1) {
    return null;
  }
  const listIndex = $from.index(listDepth);
  const listCount = listNode.childCount;
  const atStart = $from.parentOffset === 0;
  const atEnd = $from.parentOffset === $from.parent.content.size;
  return { listIndex, listCount, atStart, atEnd };
};

const handleContentKeydown = (event, editor) => {
  if (props.readOnly) {
    return false;
  }
  if (event.key === "ArrowDown") {
    const ctx = getListContext(editor);
    if (ctx && ctx.listIndex === ctx.listCount - 1 && ctx.atEnd) {
      event.preventDefault();
      props.onFocusNextTaskFromContent(props.task);
      return true;
    }
  }
  if (event.key === "ArrowUp") {
    const ctx = getListContext(editor);
    if (ctx && ctx.listIndex === 0 && ctx.atStart) {
      event.preventDefault();
      titleEditorRef.value?.focus();
      return true;
    }
  }
  return false;
};

const toggleComplete = () => {
  if (props.readOnly) {
    return;
  }
  props.onComplete(props.task);
};

const handleSplitToNewTask = async (payload) => {
  if (props.readOnly) {
    return;
  }
  if (payload?.remainingContent) {
    content.value = payload.remainingContent;
    markDirty();
  }
  await saveNow();
  props.onSplitToNewTask(props.task, {
    title: payload?.title,
    content: payload?.content
  });
};

const noopSplit = () => {};

watch(
  () => props.focusTitleId,
  async (id) => {
    if (!id || id !== props.task.id) {
      return;
    }
    await nextTick();
    titleEditorRef.value?.focus();
  }
);

watch(
  () => props.focusContentTarget,
  async (target) => {
    if (!target || target.taskId !== props.task.id) {
      return;
    }
    await nextTick();
    contentEditorRef.value?.focusListItem(target.listIndex, target.atEnd ? "end" : "start");
  }
);

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
  gap: 8px;
  padding: 4px 0;
  border: none;
  background: transparent;
  align-items: start;
}

.task-item.completed {
  color: #8a8177;
  text-decoration: line-through;
}

.task-check {
  display: grid;
  place-items: center;
  align-self: start;
  padding-top: 2px;
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
  gap: 2px;
}

.title-row {
  display: flex;
  align-items: center;
  gap: 6px;
}

.title-editor {
  flex: 1;
}

.title-editor :deep(.editor-surface) {
  padding: 0;
}

.title-editor :deep(.ProseMirror) {
  font-weight: 600;
  font-size: 1rem;
  min-height: 1.2em;
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
