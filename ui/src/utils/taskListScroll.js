export const keepTaskListsLeft = (event) => {
  const target = event.target;
  if (target?.scrollLeft && target.matches?.('.list-card, .task-list, .task-list-group, .rich-editor, .editor-surface, .ProseMirror')) {
    target.scrollLeft = 0;
  }
};
