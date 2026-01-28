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
  getActiveListItemType,
  getDefaultListTypeName,
  getListItemTypeNameForListType,
  getListItemDepth,
  isListItemEmpty,
  handleEmptyListItemBackspace,
  hasList,
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
    attributes: {
      spellcheck: "false",
      autocorrect: "off",
      autocapitalize: "off",
      autocomplete: "off"
    },
    handleDOMEvents: {
      blur(view) {
        if (!isTitle.value) {
          const doc = view.state.doc;
          const listNode = doc.childCount > 0 ? doc.child(0) : null;
          if (listNode && (listNode.type.name === "bulletList" || listNode.type.name === "taskList")) {
            const hasItems = listNode.childCount > 0;
            const hasContent = hasItems && Array.from({ length: listNode.childCount })
              .some((_, index) => !isListItemEmpty(listNode.child(index)));
            if (!hasContent) {
              editorRef.value?.commands.setContent({
                type: "doc",
                content: [{ type: "paragraph" }]
              });
            }
          }
        }
        return false;
      }
    },
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
      if (hasList(editor) || !isDocEmptyParagraph(editor.state.doc)) {
        return false;
      }
      const { schema } = editor.state;
      const listTypeName = getDefaultListTypeName(schema);
      const listItemTypeName = getListItemTypeNameForListType(listTypeName);
      const listType = schema.nodes[listTypeName];
      const listItemType = schema.nodes[listItemTypeName];
      const paragraphType = schema.nodes.paragraph;
      if (!listType || !listItemType || !paragraphType) {
        return false;
      }
      const textNode = text ? schema.text(text) : null;
      const paragraph = paragraphType.create(null, textNode ? [textNode] : undefined);
      const attrs = listItemTypeName === "taskItem" ? { checked: false } : null;
      const listItem = listItemType.create(attrs, paragraph);
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
          const listItemType = getActiveListItemType(editor);
          const split = listItemType ? editor?.commands.splitListItem(listItemType) : false;
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
        if (event.key === "1" && !isTitle.value) {
          if (toggleCheckboxAtSelection(editor)) {
            event.preventDefault();
            return true;
          }
        }
        if (event.key === "2") {
          const handled = isTitle.value
            ? toggleStarInTitle(editor)
            : toggleStarAtSelection(editor);
          if (handled) {
            event.preventDefault();
            return true;
          }
        }
        if (event.key === "3") {
          event.preventDefault();
          editor?.chain().focus().toggleHighlight({ color: "green" }).run();
          return true;
        }
        if (event.key === "4") {
          event.preventDefault();
          editor?.chain().focus().toggleHighlight({ color: "yellow" }).run();
          return true;
        }
        if (event.key === "5") {
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
          const listItemType = getActiveListItemType(editor) || "listItem";
          return editor?.commands.liftListItem(listItemType) ?? false;
        }
        const listItemType = getActiveListItemType(editor) || "listItem";
        return editor?.commands.sinkListItem(listItemType) ?? false;
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

const CHECKBOX_EMPTY = "☐";
const CHECKBOX_CHECKED = "☑";
const STAR_MARK = "⭐";
const LEGACY_STAR_MARK = "★";
const STAR_MARKS = new Set([STAR_MARK, LEGACY_STAR_MARK]);

const parsePrefix = (text) => {
  let index = 0;
  let checkboxState = null;
  let hasStar = false;
  if (text.startsWith(CHECKBOX_EMPTY)) {
    checkboxState = "empty";
    index = 1;
    if (text[index] === " ") {
      index += 1;
    }
  } else if (text.startsWith(CHECKBOX_CHECKED)) {
    checkboxState = "checked";
    index = 1;
    if (text[index] === " ") {
      index += 1;
    }
  }
  const starChar = text.slice(index, index + 1);
  if (STAR_MARKS.has(starChar)) {
    hasStar = true;
    index += 1;
    if (text[index] === " ") {
      index += 1;
    }
  }
  return { checkboxState, hasStar, prefixLength: index };
};

const buildPrefix = (checkboxState, hasStar) => {
  let prefix = "";
  if (checkboxState === "empty") {
    prefix += `${CHECKBOX_EMPTY} `;
  } else if (checkboxState === "checked") {
    prefix += `${CHECKBOX_CHECKED} `;
  }
  if (hasStar) {
    prefix += `${STAR_MARK} `;
  }
  return prefix;
};

const getListItemParagraph = (editor, listItemDepth) => {
  const { $from } = editor.state.selection;
  const listItemNode = $from.node(listItemDepth);
  const paragraph = listItemNode?.child(0);
  if (!paragraph || paragraph.type.name !== "paragraph") {
    return null;
  }
  return paragraph;
};

const applyPrefixChange = (editor, prefixLength, nextPrefix) => {
  const { state, view } = editor;
  const { selection } = state;
  const listItemDepth = getListItemDepth(editor);
  if (!listItemDepth) {
    return false;
  }
  const insertPos = state.selection.$from.start(listItemDepth) + 1;
  return applyPrefixChangeAt(editor, insertPos, prefixLength, nextPrefix, selection);
};

const applyPrefixChangeAt = (editor, insertPos, prefixLength, nextPrefix, selection) => {
  const { state, view } = editor;
  let tr = state.tr;
  if (prefixLength > 0) {
    tr = tr.delete(insertPos, insertPos + prefixLength);
  }
  if (nextPrefix) {
    tr = tr.insertText(nextPrefix, insertPos);
  }
  if (selection) {
    const mappedSelection = selection.map(tr.doc, tr.mapping);
    tr.setSelection(mappedSelection);
  }
  view.dispatch(tr);
  editor.commands.focus();
  return true;
};

const toggleCheckboxAtSelection = (editor) => {
  if (!editor) {
    return false;
  }
  const listItemDepth = getListItemDepth(editor);
  if (!listItemDepth) {
    return false;
  }
  const paragraph = getListItemParagraph(editor, listItemDepth);
  if (!paragraph) {
    return false;
  }
  const text = paragraph.textContent || "";
  const { checkboxState, hasStar, prefixLength } = parsePrefix(text);
  let nextCheckbox = null;
  if (!checkboxState) {
    nextCheckbox = "empty";
  } else if (checkboxState === "empty") {
    nextCheckbox = "checked";
  }
  const nextPrefix = buildPrefix(nextCheckbox, hasStar);
  return applyPrefixChange(editor, prefixLength, nextPrefix);
};

const toggleStarAtSelection = (editor) => {
  if (!editor) {
    return false;
  }
  const listItemDepth = getListItemDepth(editor);
  if (!listItemDepth) {
    return false;
  }
  const paragraph = getListItemParagraph(editor, listItemDepth);
  if (!paragraph) {
    return false;
  }
  const text = paragraph.textContent || "";
  const { checkboxState, hasStar, prefixLength } = parsePrefix(text);
  const nextPrefix = buildPrefix(checkboxState, !hasStar);
  return applyPrefixChange(editor, prefixLength, nextPrefix);
};

const toggleStarInTitle = (editor) => {
  if (!editor) {
    return false;
  }
  const { state } = editor;
  const doc = state.doc;
  if (doc.childCount === 0) {
    editor.commands.setContent({ type: "doc", content: [{ type: "paragraph" }] });
  }
  const firstNode = editor.state.doc.child(0);
  if (!firstNode || firstNode.type.name !== "paragraph") {
    return false;
  }
  const text = firstNode.textContent || "";
  let prefixLength = 0;
  let hasStar = false;
  const starChar = text.slice(0, 1);
  if (STAR_MARKS.has(starChar)) {
    hasStar = true;
    prefixLength = 1;
    if (text[1] === " ") {
      prefixLength = 2;
    }
  }
  const nextPrefix = hasStar ? "" : `${STAR_MARK} `;
  const insertPos = 1;
  return applyPrefixChangeAt(editor, insertPos, prefixLength, nextPrefix, state.selection);
};

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
  if (!listNode || (listNode.type.name !== "bulletList" && listNode.type.name !== "taskList")) {
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



