import { isAtDocumentEdge, joinFirstContentLine } from "../utils/taskJoin.js";
import { nextTick, onBeforeUnmount } from "vue";
import { TextSelection } from "prosemirror-state";
import { isDocEmptyJson, isListItemEmpty } from "../utils/taskDocUtils.js";
import { getListItemDepth, isListNodeName } from "../utils/editorListUtils.js";
import { splitTitleDocAtOffsets } from "../utils/titleSplitUtils.js";
import { emptyContentDoc, normalizeContent } from "../utils/taskUtils.js";

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
  let creatingBelow = null;

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

  const currentTaskSnapshot = () => options.getTaskSnapshot?.() || ({
    ...props.task,
    title: titleRef.value,
    content: contentRef.value
  });

  const getListContext = (editor) => {
    if (!editor) {
      return null;
    }
    const { $from, empty } = editor.state.selection;
    if (!empty) {
      return null;
    }
    const listItemDepth = getListItemDepth(editor);
    if (!listItemDepth) {
      return null;
    }
    const listDepth = listItemDepth - 1;
    const listNode = $from.node(listDepth);
    if (!listNode || !isListNodeName(listNode.type.name) || listDepth !== 1) {
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
    const listItemDepth = getListItemDepth(editor);
    if (!listItemDepth) {
      return false;
    }
    const listDepth = listItemDepth - 1;
    const listNode = $from.node(listDepth);
    if (!listNode || !isListNodeName(listNode.type.name)) {
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
    const listItemDepth = getListItemDepth(editor);
    if (!listItemDepth) {
      return false;
    }
    const listDepth = listItemDepth - 1;
    const listNode = $from.node(listDepth);
    if (!listNode || !isListNodeName(listNode.type.name) || listNode.childCount !== 1) {
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

  let joining = false;
  const join = (event, action) => {
    event.preventDefault();
    if (joining) return true;
    joining = true;
    if (pendingCreateTimer) { clearTimeout(pendingCreateTimer); pendingCreateTimer = null; }
    void (async () => {
      try {
        if (await saveNow() !== false) await action();
      } catch (error) {
        if (typeof window !== 'undefined') window.alert?.(error?.message || 'Could not join the lines.');
      } finally { joining = false; }
    })();
    return true;
  };

  const handleTitleKeydown = (event, editor) => {
    if (props.readOnly) {
      return false;
    }
    if ((event.ctrlKey || event.metaKey) && !event.shiftKey && !event.altKey && event.key === "1") {
      event.preventDefault();
      if (!props.allowToggle) {
        return true;
      }
      void saveNow().then((saved) => {
        if (saved !== false) props.onComplete(currentTaskSnapshot());
      });
      return true;
    }
    if (event.key === "Backspace" || event.key === "Delete") {
      if (isTitleEmpty(editor) && isContentEmpty(null)) {
        event.preventDefault();
        (options.onDelete || props.onDelete)(props.task, { direction: event.key === "Delete" ? "forward" : "backward" });
        return true;
      }
      if (event.key === "Delete" && !event.ctrlKey && !event.metaKey && !event.altKey && !event.shiftKey && isAtDocumentEdge(editor, true)) {
        const merged = joinFirstContentLine(titleRef.value, contentRef.value);
        if (merged) return join(event, async () => {
          titleRef.value = merged.title;
          contentRef.value = merged.content;
          options.onContentChanged?.();
          await nextTick();
          titleEditorRef.value?.focus(merged.selection);
          await saveNow(true);
        });
        if (props.onMergeWithNext) return join(event, () => props.onMergeWithNext(currentTaskSnapshot()));
      }
      if (event.key === "Backspace" && !event.ctrlKey && !event.metaKey && !event.altKey && !event.shiftKey && isAtDocumentEdge(editor, false) && props.onMergeToPrevious) {
        return join(event, () => props.onMergeToPrevious(currentTaskSnapshot()));
      }
    }
    if (event.key === "Tab") {
      event.preventDefault();
      if (pendingCreateTimer || creatingBelow) {
        if (pendingCreateTimer) {
          clearTimeout(pendingCreateTimer);
          pendingCreateTimer = null;
          options.revealContent?.();
          const current = normalizeContent(contentRef.value);
          const items = current?.content?.[0]?.type === "bulletList" ? current.content[0].content : [];
          const nonempty = items.filter((item) => !isDocEmptyJson(item));
          contentRef.value = { type: "doc", content: [{ type: "bulletList", content: [...nonempty, { type: "listItem", content: [{ type: "paragraph" }] }] }] };
          options.onContentChanged?.();
          void nextTick().then(() => { contentEditorRef.value?.focusListItem(nonempty.length); void saveNow(true); });
        } else {
          void creatingBelow.then((newId) => {
            if (newId) props.onTabToPrevious({ ...props.task, id: newId, title: { type: "doc", content: [{ type: "paragraph" }] }, content: emptyContentDoc() });
          });
        }
        return true;
      }
      void saveNow().then((saved) => {
        if (saved === false) return;
        props.onTabToPrevious(currentTaskSnapshot()).then((moved) => {
          if (!moved) {
            contentEditorRef.value?.insertParagraphIfEmpty();
          }
        });
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
      if (editor) {
        const { selection } = editor.state;
        const inParagraph = selection.$from?.parent?.type?.name === "paragraph"
          && selection.$from.parent === selection.$to.parent;
        if (inParagraph && (!selection.empty || !isSelectionAtEnd(editor))) {
          const fromOffset = selection.$from.parentOffset;
          const toOffset = selection.$to.parentOffset;
          const currentDoc = editor.getJSON();
          const split = splitTitleDocAtOffsets(currentDoc, fromOffset, toOffset);
          if (!isDocEmptyJson(split.after)) {
            const remainingContent = contentRef.value;
            const clearedContent = emptyContentDoc();
            titleRef.value = split.before;
            contentRef.value = clearedContent;
            editor.commands.setContent(split.before);
            saveNow(true, { suppressUndo: true });
            if (props.onSplitTitleToNewTask) {
              props.onSplitTitleToNewTask(props.task, props.categoryId, {
                beforeTitle: currentDoc,
                beforeContent: remainingContent,
                afterTitle: split.before,
                afterContent: clearedContent,
                newTitle: split.after,
                newContent: remainingContent
              });
            } else {
              props.onCreateBelow(props.task, props.categoryId, {
                title: split.after,
                content: remainingContent
              });
            }
            return true;
          }
        }
      }
      saveNow();
      pendingCreateTimer = setTimeout(() => {
        pendingCreateTimer = null;
        creatingBelow = Promise.resolve(props.onCreateBelow(props.task, props.categoryId));
        creatingBelow.finally(() => { creatingBelow = null; });
      }, 250);
      return true;
    }

    return false;
  };

  const handleContentKeydown = (event, editor) => {
    if (props.readOnly) {
      return false;
    }
    if (event.key === "Delete" && !event.ctrlKey && !event.metaKey && !event.altKey && !event.shiftKey
      && isAtDocumentEdge(editor, true) && props.onMergeWithNext
      && !(isContentEmpty(editor) && isTitleEmpty(null))) {
      return join(event, () => props.onMergeWithNext(currentTaskSnapshot()));
    }
    if (event.key === "Backspace" || event.key === "Delete") {
      if (removeSingleEmptyList(editor)) {
        event.preventDefault();
        titleEditorRef.value?.focus();
        return true;
      }
      if (isContentEmpty(editor) && isTitleEmpty(null)) {
        event.preventDefault();
        (options.onDelete || props.onDelete)(props.task, { direction: event.key === "Delete" ? "forward" : "backward" });
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
