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
import { NodeSelection, TextSelection } from "prosemirror-state";
import StarterKit from "@tiptap/starter-kit";
import Highlight from "@tiptap/extension-highlight";
import Image from "@tiptap/extension-image";
import { apiUpload } from "../api";

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

const ResizableImage = Image.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      width: {
        default: null,
        parseHTML: (element) => {
          const attr = element.getAttribute("data-width");
          if (attr) {
            const value = Number.parseInt(attr, 10);
            return Number.isNaN(value) ? null : value;
          }
          const styleWidth = element.style?.width;
          if (styleWidth && styleWidth.endsWith("px")) {
            const value = Number.parseInt(styleWidth, 10);
            return Number.isNaN(value) ? null : value;
          }
          return null;
        },
        renderHTML: (attributes) => {
          if (!attributes.width) {
            return {};
          }
          return {
            "data-width": attributes.width,
            style: `width: ${attributes.width}px;`
          };
        }
      }
    };
  },
  addNodeView() {
    return ({ node, editor, getPos }) => {
      const wrapper = document.createElement("span");
      wrapper.className = "image-resize-wrapper";
      wrapper.contentEditable = "false";

      const img = document.createElement("img");
      img.src = node.attrs.src;
      if (node.attrs.alt) {
        img.alt = node.attrs.alt;
      }
      if (node.attrs.title) {
        img.title = node.attrs.title;
      }
      if (node.attrs.width) {
        wrapper.style.width = `${node.attrs.width}px`;
      }

      const handle = document.createElement("span");
      handle.className = "image-resize-handle";

      const updateSelected = () => {
        const selection = editor.state.selection;
        const isSelected = selection instanceof NodeSelection
          && typeof getPos === "function"
          && selection.from === getPos();
        wrapper.classList.toggle("is-selected", isSelected);
      };

      const selectNode = () => {
        if (typeof getPos === "function") {
          editor.chain().focus().setNodeSelection(getPos()).run();
        }
        updateSelected();
      };

      const onSelectionUpdate = () => updateSelected();

      img.addEventListener("click", selectNode);
      handle.addEventListener("click", selectNode);
      editor.on("selectionUpdate", onSelectionUpdate);

      const onPointerDown = (event) => {
        event.preventDefault();
        event.stopPropagation();
        selectNode();
        const startX = event.clientX;
        const startWidth = wrapper.getBoundingClientRect().width || img.getBoundingClientRect().width;

        const onPointerMove = (moveEvent) => {
          const delta = moveEvent.clientX - startX;
          const nextWidth = Math.max(60, Math.round(startWidth + delta));
          wrapper.style.width = `${nextWidth}px`;
          if (typeof getPos === "function") {
            editor.commands.command(({ tr }) => {
              tr.setNodeMarkup(getPos(), undefined, { ...node.attrs, width: nextWidth });
              return true;
            });
          }
        };

        const onPointerUp = () => {
          if (typeof getPos === "function") {
            editor.chain().focus().setNodeSelection(getPos()).run();
            updateSelected();
          }
          document.removeEventListener("pointermove", onPointerMove);
          document.removeEventListener("pointerup", onPointerUp);
        };

        document.addEventListener("pointermove", onPointerMove);
        document.addEventListener("pointerup", onPointerUp);
      };

      handle.addEventListener("pointerdown", onPointerDown);

      wrapper.appendChild(img);
      wrapper.appendChild(handle);

      return {
        dom: wrapper,
        update: (updatedNode) => {
          if (updatedNode.type.name !== node.type.name) {
            return false;
          }
          img.src = updatedNode.attrs.src;
          img.alt = updatedNode.attrs.alt || "";
          img.title = updatedNode.attrs.title || "";
          wrapper.style.width = updatedNode.attrs.width ? `${updatedNode.attrs.width}px` : "";
          node = updatedNode;
          updateSelected();
          return true;
        },
        destroy: () => {
          handle.removeEventListener("pointerdown", onPointerDown);
          img.removeEventListener("click", selectNode);
          handle.removeEventListener("click", selectNode);
          editor.off("selectionUpdate", onSelectionUpdate);
        }
      };
    };
  }
});

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
    const response = await apiUpload("/api/attachments", formData);
    if (!response?.url) {
      throw new Error("Upload failed");
    }
    editor.chain().focus().setImage({ src: response.url }).run();
  } catch (error) {
    const message = error instanceof Error ? error.message : "Image upload failed";
    window.alert(message);
  }
};

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

const getListItemDepth = (editor) => {
  if (!editor) {
    return null;
  }
  const { $from } = editor.state.selection;
  for (let depth = $from.depth; depth > 0; depth -= 1) {
    if ($from.node(depth).type.name === "listItem") {
      return depth;
    }
  }
  return null;
};

const isInListItem = (editor) => {
  const depth = getListItemDepth(editor);
  return depth !== null;
};

const hasBulletList = (editor) => {
  if (!editor) {
    return false;
  }
  const doc = editor.state.doc;
  return doc.childCount > 0 && doc.child(0).type.name === "bulletList";
};

const isDocEmptyParagraph = (doc) => {
  if (!doc) {
    return true;
  }
  if (doc.childCount === 0) {
    return true;
  }
  if (doc.childCount !== 1) {
    return false;
  }
  const child = doc.child(0);
  if (child.type.name !== "paragraph") {
    return false;
  }
  if (child.textContent.trim().length > 0) {
    return false;
  }
  let hasInlineNonText = false;
  child.descendants((node) => {
    if (node.isInline && node.type.name !== "text" && node.type.name !== "hardBreak") {
      hasInlineNonText = true;
      return false;
    }
    return true;
  });
  return !hasInlineNonText;
};

const isListItemEmpty = (listItem) => {
  if (!listItem) {
    return false;
  }
  if (listItem.textContent.trim().length > 0) {
    return false;
  }
  let hasInlineNonText = false;
  listItem.descendants((node) => {
    if (node.isInline && node.type.name !== "text" && node.type.name !== "hardBreak") {
      hasInlineNonText = true;
      return false;
    }
    return true;
  });
  return !hasInlineNonText;
};

const blockNonEmptyListItemBackspace = (editor) => {
  if (!editor) {
    return false;
  }
  const { state } = editor;
  const { selection } = state;
  if (!selection.empty) {
    return false;
  }
  const { $from } = selection;
  const listItemDepth = getListItemDepth(editor);
  if (!listItemDepth) {
    return false;
  }
  const listItem = $from.node(listItemDepth);
  if (isListItemEmpty(listItem)) {
    return false;
  }
  if ($from.parentOffset !== 0) {
    return false;
  }
  return true;
};

const handleEmptyListItemBackspace = (editor) => {
  if (!editor) {
    return false;
  }
  const { state, view } = editor;
  const { selection } = state;
  if (!selection.empty) {
    return false;
  }
  const { $from } = selection;
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
  if (!listNode || listNode.type.name !== "bulletList") {
    return false;
  }
  const listIndex = $from.index(listDepth);
  const listItem = $from.node(listItemDepth);
  if (!isListItemEmpty(listItem)) {
    return false;
  }

  if (listIndex <= 0) {
    const startPos = $from.start(listItemDepth) + 1;
    const trStart = state.tr.setSelection(TextSelection.create(state.doc, startPos));
    view.dispatch(trStart);
    editor.commands.focus();
    return true;
  }

  const listStart = $from.before(listDepth) + 1;
  let prevEnd = listStart + listNode.child(0).nodeSize - 2;
  let pos = listStart;
  for (let i = 0; i < listIndex; i += 1) {
    const child = listNode.child(i);
    if (i === listIndex - 1) {
      prevEnd = pos + child.nodeSize - 2;
      break;
    }
    pos += child.nodeSize;
  }

  const tr = state.tr.delete($from.before(listItemDepth), $from.after(listItemDepth));
  tr.setSelection(TextSelection.create(tr.doc, Math.max(prevEnd, 1)));
  view.dispatch(tr);
  editor.commands.focus();
  return true;
};

const insertListItemAfterSelection = (editor) => {
  if (!editor) {
    return false;
  }
  const { state, view } = editor;
  const { $from } = state.selection;
  const listItemDepth = getListItemDepth(editor);
  if (!listItemDepth) {
    return false;
  }
  const listDepth = listItemDepth - 1;
  const listNode = $from.node(listDepth);
  if (!listNode || listNode.type.name !== "bulletList") {
    return false;
  }
  const listItemType = state.schema.nodes.listItem;
  const paragraphType = state.schema.nodes.paragraph;
  if (!listItemType || !paragraphType) {
    return false;
  }
  const newItem = listItemType.createAndFill(null, paragraphType.createAndFill());
  if (!newItem) {
    return false;
  }
  const insertPos = $from.after(listItemDepth);
  const tr = state.tr.insert(insertPos, newItem);
  const selectionPos = insertPos + 2;
  tr.setSelection(TextSelection.create(tr.doc, selectionPos));
  view.dispatch(tr);
  editor.commands.focus();
  return true;
};

const appendListItem = (editor) => {
  if (!editor) {
    return false;
  }
  const { state, view } = editor;
  const { doc, schema } = state;
  const listNode = doc.childCount > 0 ? doc.child(0) : null;
  const listItemType = schema.nodes.listItem;
  const paragraphType = schema.nodes.paragraph;
  const listType = schema.nodes.bulletList;
  if (!listItemType || !paragraphType || !listType) {
    return false;
  }
  const newItem = listItemType.createAndFill(null, paragraphType.createAndFill());
  if (!newItem) {
    return false;
  }
  if (!listNode || listNode.type.name !== "bulletList") {
    const list = listType.create(null, newItem);
    const tr = state.tr.replaceWith(0, doc.content.size, list);
    const selectionPos = Math.min(tr.doc.content.size, 2);
    tr.setSelection(TextSelection.create(tr.doc, selectionPos));
    view.dispatch(tr);
    editor.commands.focus();
    return true;
  }
  const listPos = 1;
  const insertPos = listPos + listNode.nodeSize - 1;
  const tr = state.tr.insert(insertPos, newItem);
  const selectionPos = insertPos + 2;
  tr.setSelection(TextSelection.create(tr.doc, selectionPos));
  view.dispatch(tr);
  editor.commands.focus();
  return true;
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
