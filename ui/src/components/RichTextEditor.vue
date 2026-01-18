<template>
  <div class="rich-editor">
    <div class="toolbar">
      <button
        type="button"
        class="tool"
        :class="{ active: editorInstance?.isActive('bold') }"
        @mousedown.prevent
        @click="toggleBold"
      >
        B
      </button>
      <button
        type="button"
        class="tool"
        :class="{ active: editorInstance?.isActive('italic') }"
        @mousedown.prevent
        @click="toggleItalic"
      >
        I
      </button>
      <span class="tool-sep"></span>
      <button
        type="button"
        class="tool"
        :class="{ active: editorInstance?.isActive('highlight', { color: 'green' }) }"
        @mousedown.prevent
        @click="toggleHighlight('green')"
      >
        Green
      </button>
      <button
        type="button"
        class="tool"
        :class="{ active: editorInstance?.isActive('highlight', { color: 'yellow' }) }"
        @mousedown.prevent
        @click="toggleHighlight('yellow')"
      >
        Yellow
      </button>
      <button
        type="button"
        class="tool"
        :class="{ active: editorInstance?.isActive('highlight', { color: 'red' }) }"
        @mousedown.prevent
        @click="toggleHighlight('red')"
      >
        Red
      </button>
    </div>
    <EditorContent class="editor-surface" :editor="editor" />
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, watch } from "vue";
import { EditorContent, useEditor } from "@tiptap/vue-3";
import StarterKit from "@tiptap/starter-kit";
import Highlight from "@tiptap/extension-highlight";

const props = defineProps({
  modelValue: {
    type: Object,
    required: true
  },
  editable: {
    type: Boolean,
    default: true
  },
  onDirty: {
    type: Function,
    required: true
  }
});

const emit = defineEmits(["update:modelValue"]);

const editorRef = useEditor({
  content: props.modelValue,
  editable: props.editable,
  extensions: [
    StarterKit.configure({
      heading: false,
      code: false,
      codeBlock: false,
      blockquote: false,
      strike: false,
      horizontalRule: false
    }),
    Highlight.configure({
      multicolor: true
    })
  ],
  editorProps: {
    handleKeyDown(view, event) {
      const editor = editorRef?.value ?? editorRef;
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() == 'b') {
        event.preventDefault();
        editor?.chain().focus().toggleBold().run();
        return true;
      }
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() == 'i') {
        event.preventDefault();
        editor?.chain().focus().toggleItalic().run();
        return true;
      }
      if (event.key === 'Tab') {
        event.preventDefault();
        if (event.shiftKey) {
          return editor?.commands.liftListItem('listItem') ?? false;
        }
        return editor?.commands.sinkListItem('listItem') ?? false;
      }
      return false;
    }
  },
  onUpdate({ editor }) {
    const json = editor.getJSON();
    emit("update:modelValue", json);
    props.onDirty(json);
  }
});

const editor = editorRef;
const editorInstance = computed(() => editorRef?.value);

const toggleBold = () => {
  editorInstance.value?.chain().focus().toggleBold().run();
};

const toggleItalic = () => {
  editorInstance.value?.chain().focus().toggleItalic().run();
};

const toggleHighlight = (color) => {
  editorInstance.value?.chain().focus().toggleHighlight({ color }).run();
};

const focus = () => {
  editorInstance.value?.chain().focus().run();
};

const insertParagraphIfEmpty = () => {
  const editor = editorInstance.value;
  if (!editor) {
    return;
  }
  const json = editor.getJSON();
  if (!json.content || json.content.length === 0) {
    editor.commands.setContent({
      type: "doc",
      content: [{ type: "paragraph" }]
    });
    editor.commands.focus("end");
  } else {
    editor.commands.focus("end");
  }
};

watch(
  () => props.modelValue,
  (value) => {
    const editor = editorInstance.value;
    if (!editor) {
      return;
    }
    const current = editor.getJSON();
    if (JSON.stringify(current) === JSON.stringify(value)) {
      return;
    }
    editor.commands.setContent(value, false);
  }
);

onBeforeUnmount(() => {
  editorInstance.value?.destroy();
});

defineExpose({
  focus,
  insertParagraphIfEmpty
});
</script>
