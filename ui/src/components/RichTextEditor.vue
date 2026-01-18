<template>
  <div class="rich-editor">
    <div v-if="showToolbar" class="toolbar">
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
  },
  onSplitToNewTask: {
    type: Function,
    required: true
  },
  onKeyDown: {
    type: Function,
    default: null
  },
  mode: {
    type: String,
    default: "content"
  },
  showToolbar: {
    type: Boolean,
    default: false
  }
});

const emit = defineEmits(["update:modelValue"]);

const isTitle = computed(() => props.mode === "title");

const editorRef = useEditor({
  content: props.modelValue,
  editable: props.editable,
  extensions: [
    StarterKit.configure({
      heading: false,
      orderedList: false,
      bulletList: !isTitle.value,
      listItem: !isTitle.value,
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
      if (props.onKeyDown) {
        const handled = props.onKeyDown(event, editor);
        if (handled) {
          return true;
        }
      }
      if (event.key === 'Enter' && event.shiftKey) {
        event.preventDefault();
        editor?.chain().focus().setHardBreak().run();
        return true;
      }
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
      if ((event.ctrlKey || event.metaKey) && !event.shiftKey && !event.altKey) {
        if (event.key === "1") {
          event.preventDefault();
          editor?.chain().focus().toggleHighlight({ color: "green" }).run();
          return true;
        }
        if (event.key === "2") {
          event.preventDefault();
          editor?.chain().focus().toggleHighlight({ color: "yellow" }).run();
          return true;
        }
        if (event.key === "3") {
          event.preventDefault();
          editor?.chain().focus().toggleHighlight({ color: "red" }).run();
          return true;
        }
      }
      if (event.key === 'Tab') {
        event.preventDefault();
        if (isTitle.value) {
          return true;
        }
        if (event.shiftKey) {
          if (isOutermostListItem(editor)) {
            const splitPayload = splitAtSelection(editor);
            if (splitPayload) {
              props.onSplitToNewTask({
                title: splitPayload.title,
                content: splitPayload.newTaskContent,
                remainingContent: splitPayload.remainingContent
              });
              editor.commands.setContent(splitPayload.remainingContent);
              return true;
            }
          }
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

const focusListItem = (listIndex, place = "start") => {
  const editor = editorInstance.value;
  if (!editor) {
    return;
  }
  if (isTitle.value) {
    editor.commands.focus("end");
    return;
  }
  const doc = editor.state.doc;
  const listNode = doc.childCount > 0 ? doc.child(0) : null;
  if (!listNode || listNode.type.name !== "bulletList") {
    editor.commands.focus("end");
    return;
  }
  if (typeof listIndex !== "number" || listIndex < 0 || listIndex >= listNode.childCount) {
    editor.commands.focus("end");
    return;
  }
  let pos = 1;
  pos += 1;
  for (let i = 0; i < listIndex; i += 1) {
    pos += listNode.child(i).nodeSize;
  }
  let targetPos = pos + 2;
  if (place === "end") {
    targetPos = pos + listNode.child(listIndex).nodeSize - 2;
  }
  const maxPos = doc.content.size;
  if (targetPos > maxPos) {
    targetPos = maxPos;
  }
  editor.commands.setTextSelection(targetPos);
  editor.commands.focus();
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

const splitAtSelection = (editor) => {
  if (!editor) {
    return null;
  }
  const { $from } = editor.state.selection;
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
  const doc = editor.getJSON();
  const listContent = doc.content?.[0]?.content ?? [];
  if (listIndex < 0 || listIndex >= listContent.length) {
    return null;
  }
  const before = listContent.slice(0, listIndex);
  const current = listContent[listIndex];
  const after = listContent.slice(listIndex + 1);
  const titleDoc = listItemToTitleDoc(current);
  const remaining = {
    type: "doc",
    content: [
      {
        type: "bulletList",
        content: before.length
          ? before
          : [
              {
                type: "listItem",
                content: [{ type: "paragraph" }]
              }
            ]
      }
    ]
  };
  const newTaskContent = {
    type: "doc",
    content: [
      {
        type: "bulletList",
        content: after.length
          ? after
          : [
              {
                type: "listItem",
                content: [{ type: "paragraph" }]
              }
            ]
      }
    ]
  };
  return { remainingContent: remaining, newTaskContent, title: titleDoc };
};

const extractPlainText = (node) => {
  if (!node) {
    return "";
  }
  if (Array.isArray(node)) {
    return node.map(extractPlainText).join(" ");
  }
  if (typeof node === "object") {
    if (node.text) {
      return node.text;
    }
    if (node.type === "hardBreak") {
      return " ";
    }
    if (node.content) {
      return extractPlainText(node.content);
    }
  }
  return "";
};

const listItemToTitleDoc = (listItem) => {
  const paragraphs = [];
  if (listItem?.content) {
    for (const node of listItem.content) {
      if (node.type === "paragraph") {
        paragraphs.push(node);
      }
    }
  }
  const inlineContent = [];
  paragraphs.forEach((para, index) => {
    if (para.content) {
      inlineContent.push(...para.content);
    }
    if (index < paragraphs.length - 1) {
      inlineContent.push({ type: "hardBreak" });
    }
  });
  return {
    type: "doc",
    content: [
      {
        type: "paragraph",
        content: inlineContent.length ? inlineContent : []
      }
    ]
  };
};

const isOutermostListItem = (editor) => {
  if (!editor) {
    return false;
  }
  const { $from } = editor.state.selection;
  let listItemDepth = null;
  for (let depth = $from.depth; depth > 0; depth -= 1) {
    if ($from.node(depth).type.name === "listItem") {
      listItemDepth = depth;
      break;
    }
  }
  if (!listItemDepth) {
    return false;
  }
  const listDepth = listItemDepth - 1;
  const listNode = $from.node(listDepth);
  return listNode?.type?.name === "bulletList" && listDepth === 1;
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
  focusListItem,
  insertParagraphIfEmpty
});
</script>
