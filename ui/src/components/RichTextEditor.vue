<template>
  <div class="rich-editor">
    <RichTextToolbar v-if="showToolbar" :editor="editorInstance" />
    <EditorContent class="editor-surface" :editor="editor" />
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, watch } from "vue";
import { EditorContent, useEditor } from "@tiptap/vue-3";
import { TextSelection } from "prosemirror-state";
import StarterKit from "@tiptap/starter-kit";
import Highlight from "@tiptap/extension-highlight";
import { uploadAttachment } from "../api/attachments.js";
import RichTextToolbar from "./RichTextToolbar.vue";
import {
  appendListItem,
  blockNonEmptyListItemBackspace,
  handleEmptyListItemBackspace,
  hasBulletList,
  insertListItemAfterSelection,
  isDocEmptyParagraph,
  isInListItem,
  isOutermostListItem,
  splitAtSelection
} from "../utils/editorListUtils.js";
import ResizableImage from "../utils/resizableImage.js";

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
    }),
    ResizableImage.configure({
      inline: true,
      allowBase64: false
    })
  ],
  editorProps: {
    handlePaste(view, event) {
      const editor = editorRef?.value ?? editorRef;
      const items = Array.from(event.clipboardData?.items ?? []);
      const imageItem = items.find((item) => item.type && item.type.startsWith("image/"));
      if (!imageItem) {
        return false;
      }
      const file = imageItem.getAsFile();
      if (!file) {
        return false;
      }
      event.preventDefault();
      void insertImageFromFile(editor, file);
      return true;
    },
    handleDrop(view, event) {
      const editor = editorRef?.value ?? editorRef;
      const files = Array.from(event.dataTransfer?.files ?? []);
      const imageFile = files.find((file) => file.type && file.type.startsWith("image/"));
      if (!imageFile) {
        if (event.dataTransfer?.getData("text/plain")) {
          event.preventDefault();
          return true;
        }
        return false;
      }
      event.preventDefault();
      const coords = view.posAtCoords({ left: event.clientX, top: event.clientY });
      if (coords?.pos) {
        editor?.commands.setTextSelection(coords.pos);
      }
      void insertImageFromFile(editor, imageFile);
      return true;
    },
    handleTextInput(view, from, to, text) {
      const editor = editorRef?.value ?? editorRef;
      if (isTitle.value || !editor) {
        return false;
      }
      if (hasBulletList(editor) || !isDocEmptyParagraph(editor.state.doc)) {
        return false;
      }
      const { schema } = editor.state;
      const listType = schema.nodes.bulletList;
      const listItemType = schema.nodes.listItem;
      const paragraphType = schema.nodes.paragraph;
      if (!listType || !listItemType || !paragraphType) {
        return false;
      }
      const textNode = text ? schema.text(text) : null;
      const paragraph = paragraphType.create(null, textNode ? [textNode] : undefined);
      const listItem = listItemType.create(null, paragraph);
      const list = listType.create(null, listItem);
      const tr = editor.state.tr.replaceWith(0, editor.state.doc.content.size, list);
      const selectionPos = Math.min(tr.doc.content.size, 2 + text.length);
      tr.setSelection(TextSelection.create(tr.doc, selectionPos));
      view.dispatch(tr);
      editor.commands.focus();
      return true;
    },
    handleKeyDown(view, event) {
      const editor = editorRef?.value ?? editorRef;
      if (props.onKeyDown) {
        const handled = props.onKeyDown(event, editor);
        if (handled) {
          return true;
        }
      }
      if (event.key === "Backspace" && !isTitle.value) {
        if (handleEmptyListItemBackspace(editor) || blockNonEmptyListItemBackspace(editor)) {
          event.preventDefault();
          return true;
        }
      }
      if (event.key === "Enter" && !event.shiftKey && !isTitle.value) {
        if (isInListItem(editor)) {
          event.preventDefault();
          const split = editor?.commands.splitListItem("listItem");
          if (split) {
            return true;
          }
          return insertListItemAfterSelection(editor);
        }
        if (appendListItem(editor)) {
          event.preventDefault();
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

const insertImageFromFile = async (editor, file) => {
  if (!editor) {
    return;
  }
  const formData = new FormData();
  formData.append("file", file, file.name || "image");
  try {
    const response = await uploadAttachment(formData);
    if (!response?.url) {
      throw new Error("Upload failed");
    }
    editor.chain().focus().setImage({ src: response.url }).run();
  } catch (error) {
    const message = error instanceof Error ? error.message : "Image upload failed";
    window.alert(message);
  }
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

<style scoped>
.rich-editor :deep(.image-resize-wrapper) {
  display: inline-block;
  position: relative;
  max-width: 100%;
}

.rich-editor :deep(.image-resize-wrapper img) {
  display: block;
  width: 100%;
  height: auto;
  max-width: 100%;
}

.rich-editor :deep(.image-resize-handle) {
  position: absolute;
  right: -4px;
  bottom: 0;
  width: 10px;
  height: 10px;
  border-radius: 0;
  background: #1f1b16;
  cursor: nwse-resize;
  opacity: 0;
}

.rich-editor :deep(.image-resize-wrapper.is-selected .image-resize-handle) {
  opacity: 1;
}

.rich-editor :deep(.image-resize-wrapper.is-selected img) {
  outline: 2px solid #1f1b16;
  outline-offset: 2px;
}
</style>


