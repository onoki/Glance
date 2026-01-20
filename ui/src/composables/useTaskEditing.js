import { onBeforeUnmount } from "vue";
import { TextSelection } from "prosemirror-state";
import { isDocEmptyJson, isListItemEmpty } from "../utils/taskDocUtils.js";

export const useTaskEditing = (options) => {
  const {
    props,
    titleRef,
    contentRef,
    titleEditorRef,
    contentEditorRef,
    hasSubcontent,
    saveNow
  } = options;

  let pendingCreateTimer = null;

  onBeforeUnmount(() => {
    if (pendingCreateTimer) {
      clearTimeout(pendingCreateTimer);
      pendingCreateTimer = null;
    }
  });

  const isTitleEmpty = (editor) => {
    if (!editor) {
      return isDocEmptyJson(titleRef.value);
    }
    return editor.state.doc.textContent.trim().length === 0;
  };

  const isContentEmpty = (editor) => {
    if (!editor) {
      return isDocEmptyJson(contentRef.value);
    }
    let hasContent = false;
    editor.state.doc.descendants((node) => {
      if (node.isText && node.text?.trim()) {
        hasContent = true;
        return false;
      }
      if (node.isInline && node.type.name !== "text" && node.type.name !== "hardBreak") {
        hasContent = true;
        return false;
      }
      return true;
    });
    return !hasContent;
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

  const removeEmptyLastListItem = (editor) => {
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
    if (listIndex !== listNode.childCount - 1) {
      return false;
    }
    const listItem = $from.node(listItemDepth);
    if (!isListItemEmpty(listItem)) {
      return false;
    }
    if (listNode.childCount === 1) {
      editor.commands.setContent({
        type: "doc",
        content: [{ type: "paragraph" }]
      });
      return true;
    }

    const from = $from.before(listItemDepth);
    const to = $from.after(listItemDepth);
    const tr = state.tr.delete(from, to);
    const nextPos = Math.max(from - 1, 1);
    tr.setSelection(TextSelection.create(tr.doc, nextPos));
    view.dispatch(tr);
    editor.commands.focus();
    return true;
  };

  const removeSingleEmptyList = (editor) => {
    if (!editor) {
      return false;
    }
    const { state } = editor;
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
    if (!listNode || listNode.type.name !== "bulletList" || listNode.childCount !== 1) {
      return false;
    }
    const listItem = $from.node(listItemDepth);
    if (!isListItemEmpty(listItem)) {
      return false;
    }
    editor.commands.setContent({
      type: "doc",
      content: [{ type: "paragraph" }]
    });
    return true;
  };

  const handleTitleKeydown = (event, editor) => {
    if (props.readOnly) {
      return false;
    }
    if (event.key === "Backspace" && isTitleEmpty(editor) && isContentEmpty(null)) {
      event.preventDefault();
      props.onDelete(props.task);
      return true;
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
      if (hasSubcontent.value) {
        contentEditorRef.value?.focusListItem(0, "start");
        return true;
      }
      props.onFocusNextTaskFromContent(props.task);
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
        props.onCreateBelow(props.task, props.categoryId);
      }, 250);
      return true;
    }

    return false;
  };

  const handleContentKeydown = (event, editor) => {
    if (props.readOnly) {
      return false;
    }
    if (event.key === "Backspace") {
      if (removeSingleEmptyList(editor)) {
        event.preventDefault();
        titleEditorRef.value?.focus();
        return true;
      }
      if (isContentEmpty(editor) && isTitleEmpty(null)) {
        event.preventDefault();
        props.onDelete(props.task);
        return true;
      }
    }
    if (event.key === "Enter" && !event.shiftKey) {
      if (props.isLastInCategory && removeEmptyLastListItem(editor)) {
        event.preventDefault();
        props.onCreateBelow(props.task, props.categoryId);
        return true;
      }
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

  return {
    handleTitleKeydown,
    handleContentKeydown
  };
};
