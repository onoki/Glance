import { atVerticalDocumentEdge, verticalCaretIntent } from "../utils/taskNavigation.js";
import { isAtDocumentEdge, joinFirstContentLine, lastParagraph } from "../utils/taskJoin.js";
import { nextTick, onBeforeUnmount } from "vue";
import { isDocEmptyJson, isListItemEmpty, isSingleEmptyBullet } from "../utils/taskDocUtils.js";
import { getListItemDepth, isListNodeName, removeEmptyListItem, removeBlankBulletBoundary } from "../utils/editorListUtils.js";
import { splitTitleDocAtOffsets } from "../utils/titleSplitUtils.js";
import { emptyContentDoc, mergeTitleDocs, normalizeContent } from "../utils/taskUtils.js";

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
  let pendingSubcontentFocus = false;

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
    return isDocEmptyJson(editor.getJSON?.() || editor.state.doc.toJSON?.() || titleRef.value);
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

  const currentTaskSnapshot = () => options.getTaskSnapshot?.() || ({
    ...props.task,
    title: titleRef.value,
    content: contentRef.value
  });

  const removeEmptyLastListItem = (editor) => {
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
    if (listDepth !== 1 || $from.index(0) !== state.doc.childCount - 1) return false;
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
    return removeEmptyListItem(editor);
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
    if (listDepth !== 1 || state.doc.childCount !== 1) return false;
    const listNode = $from.node(listDepth);
    if (!listNode || !isListNodeName(listNode.type.name) || listNode.childCount !== 1) {
      return false;
    }
    const listItem = $from.node(listItemDepth);
    if (!isListItemEmpty(listItem)) {
      return false;
    }
    return removeEmptyListItem(editor);
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

  const handleHorizontalArrow = (event, editor, area) => {
    if (props.readOnly || event.shiftKey || event.ctrlKey || event.altKey || event.metaKey || event.isComposing) return false;
    const direction = event.key === 'ArrowLeft' ? -1 : event.key === 'ArrowRight' ? 1 : 0;
    if (!direction || !isAtDocumentEdge(editor, direction > 0)) return false;
    if (area === 'title' && direction > 0 && hasSubcontent.value) {
      event.preventDefault();
      contentEditorRef.value?.focusListItem(0, 'start');
    } else if (area === 'content' && direction < 0) {
      event.preventDefault();
      const position = lastParagraph(titleRef.value)?.end || 1;
      titleEditorRef.value?.focus({ from: position, to: position });
    } else {
      if (!props.onNavigateHorizontal) return false;
      event.preventDefault();
      props.onNavigateHorizontal(props.task, direction);
    }
    return true;
  };

  const isPlainArrow = event => !event.shiftKey && !event.ctrlKey && !event.metaKey && !event.altKey && !event.isComposing;

  const handleTitleKeydown = (event, editor) => {
    if (handleHorizontalArrow(event, editor, "title")) return true;
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
    if (event.key === "Tab" && pendingSubcontentFocus) {
      event.preventDefault();
      return true; // The new first bullet has no preceding sibling to indent beneath.
    }
    if (event.key === "Tab") {
      event.preventDefault();
      if (joining) return true;
      if (pendingCreateTimer || creatingBelow) {
        if (pendingCreateTimer) {
          clearTimeout(pendingCreateTimer);
          pendingCreateTimer = null;
          options.revealContent?.();
          const current = normalizeContent(contentRef.value);
          const items = current?.content?.[0]?.type === "bulletList" ? current.content[0].content : [];
          const reuse = isSingleEmptyBullet(current);
          if (!reuse) {
            contentRef.value = { type: "doc", content: [{ type: "bulletList", content: [...items, { type: "listItem", content: [{ type: "paragraph" }] }] }] };
            options.onContentChanged?.();
          }
          void nextTick().then(() => {
            contentEditorRef.value?.focusListItem(reuse ? 0 : items.length);
            if (!reuse) void saveNow(true);
          });
        } else {
          const creation = creatingBelow;
          return join(event, async () => {
            const newId = await creation;
            if (newId) await props.onTabToPrevious({ ...props.task, id: newId, title: { type: "doc", content: [{ type: "paragraph" }] }, content: emptyContentDoc() });
          });
        }
        return true;
      }
      return join(event, async () => {
        const moved = await props.onTabToPrevious(currentTaskSnapshot());
        if (moved === false) contentEditorRef.value?.insertParagraphIfEmpty();
      });
    }

    if (isPlainArrow(event) && event.key === "ArrowDown" && atVerticalDocumentEdge(editor, "down")) {
      event.preventDefault();
      if (hasSubcontent.value) contentEditorRef.value?.focusListItem(0, "end");
      else props.onFocusNextTaskFromContent(props.task, verticalCaretIntent(editor));
      return true;
    }
    if (isPlainArrow(event) && event.key === "ArrowUp" && atVerticalDocumentEdge(editor, "up")) {
      event.preventDefault();
      props.onFocusPrevTaskFromTitle(props.task, verticalCaretIntent(editor));
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
        // At the start, insert a blank row rather than clearing this note and
        // copying its entire document into a second, fallible create request.
        if (inParagraph && selection.empty && selection.from === 1 && !isTitleEmpty(editor)) {
          return join(event, () => props.onCreateBelow(currentTaskSnapshot(), props.categoryId, null, { before: true }));
        }
        if (inParagraph && (!selection.empty || !isSelectionAtEnd(editor))) {
          const fromOffset = selection.$from.parentOffset;
          const toOffset = selection.$to.parentOffset;
          const currentDoc = editor.getJSON();
          const sourceTitleSnapshot = JSON.stringify(titleRef.value);
          const split = splitTitleDocAtOffsets(currentDoc, fromOffset, toOffset);
          if (!isDocEmptyJson(split.after)) {
            const remainingContent = JSON.parse(JSON.stringify(contentRef.value));
            const clearedContent = emptyContentDoc();
            return join(event, async () => {
              const commitSource = async () => {
                // The destination must already be durable before clearing the
                // suffix. Do not overwrite typing made during a slow creation.
                if (JSON.stringify(titleRef.value) !== sourceTitleSnapshot
                  || JSON.stringify(contentRef.value) !== JSON.stringify(remainingContent)) {
                  throw new Error('The title changed while splitting. Both notes have been kept; review the new copy.');
                }
                titleRef.value = split.before;
                contentRef.value = clearedContent;
                editor.commands.setContent(split.before);
                const shortenedTitleSnapshot = JSON.stringify(titleRef.value);
                const saved = await saveNow(true, { suppressUndo: true });
                if (saved === false) {
                  // Undo of the partial operation removes only its copy. Restore
                  // a complete local source first, so a later retry cannot save
                  // the shortened source and then discard the only suffix copy.
                  titleRef.value = JSON.stringify(titleRef.value) === shortenedTitleSnapshot
                    ? JSON.parse(sourceTitleSnapshot) : mergeTitleDocs(titleRef.value, split.after);
                  if (JSON.stringify(contentRef.value) === JSON.stringify(clearedContent)) contentRef.value = remainingContent;
                  else {
                    const items = doc => normalizeContent(doc).content[0].type === 'bulletList' ? normalizeContent(doc).content[0].content : [];
                    contentRef.value = normalizeContent({ type: 'doc', content: [{ type: 'bulletList', content: [...items(contentRef.value), ...items(remainingContent)] }] });
                  }
                  editor.commands.setContent(titleRef.value);
                  options.onContentChanged?.();
                }
                return saved;
              };
              if (props.onSplitTitleToNewTask) await props.onSplitTitleToNewTask(currentTaskSnapshot(), props.categoryId, {
                beforeTitle: currentDoc,
                beforeContent: remainingContent,
                afterTitle: split.before,
                afterContent: clearedContent,
                newTitle: split.after,
                newContent: remainingContent,
                commitSource
              });
              else {
                const newId = await props.onCreateBelow(currentTaskSnapshot(), props.categoryId, { title: split.after, content: remainingContent });
                if (newId && await commitSource() === false) throw new Error('The new note is saved, but the original could not be updated. Both versions have been kept.');
              }
            });
          }
        }
      }
      const reuse = isSingleEmptyBullet(contentRef.value);
      if (editor && editor.state.selection.empty && isSelectionAtEnd(editor) && (hasSubcontent.value || reuse)) {
        event.preventDefault();
        options.revealContent?.();
        if (!reuse) {
          const content = JSON.parse(JSON.stringify(normalizeContent(contentRef.value)));
          content.content[0].content.unshift({ type: "listItem", content: [{ type: "paragraph" }] });
          contentRef.value = content;
          options.onContentChanged?.();
        }
        pendingSubcontentFocus = true;
        void nextTick().then(() => {
          contentEditorRef.value?.focusListItem(0, "start");
          if (typeof requestAnimationFrame === "function") requestAnimationFrame(() => { pendingSubcontentFocus = false; });
          else pendingSubcontentFocus = false;
          if (!reuse) void saveNow(true);
        });
        return true;
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
    if (handleHorizontalArrow(event, editor, "content")) return true;
    if (props.readOnly) {
      return false;
    }
    if (!event.ctrlKey && !event.metaKey && !event.altKey && !event.shiftKey && removeBlankBulletBoundary(editor, event.key)) {
      event.preventDefault();
      return true;
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
    if (isPlainArrow(event) && event.key === "ArrowDown" && atVerticalDocumentEdge(editor, "down")) {
      event.preventDefault();
      props.onFocusNextTaskFromContent(props.task, verticalCaretIntent(editor));
      return true;
    }
    if (isPlainArrow(event) && event.key === "ArrowUp" && atVerticalDocumentEdge(editor, "up")) {
      event.preventDefault();
      const position = lastParagraph(titleRef.value)?.end || 1;
      titleEditorRef.value?.focus({ from: position, to: position });
      return true;
    }
    return false;
  };

  return {
    handleTitleKeydown,
    handleContentKeydown
  };
};
