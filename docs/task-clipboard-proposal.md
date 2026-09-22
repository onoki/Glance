# Whole-task clipboard

Implemented in Dashboard and People. History and Search retain their existing behavior.

## Selecting tasks

The narrow dotted handle to the left of the completion checkbox selects an entire task: formatted title, all subcontent, nested lists, links, and attachment references. The handle is 8px wide with a 2px gap, adding 10px to each editable row. Selection changes the background, without changing row height. Dragging this same handle reorders the task using the existing drop indicator; a separate reorder button is unnecessary.

- Click selects one task. Ctrl-click toggles additional tasks. Shift-click selects a contiguous range in visual order.
- Ctrl+Shift+Space selects the current task from its text editor.
- On the handle, Up/Down changes task selection; Shift+Up/Down extends a range.
- Escape returns to text editing. Clicking into text clears whole-task selection.
- The completion checkbox remains separate and never selects a task.

## Copy, cut, paste, and Undo

Ctrl+C copies selected tasks; Ctrl+X cuts them after the system confirms clipboard writing and pending edits have saved. Ordinary text selection still uses normal text copy/cut/paste. The clipboard includes versioned Glance data in an HTML attribute plus readable HTML and plain text for other applications.

Ctrl+V with a Glance task payload inserts complete tasks below the focused or selected task. With no task focused, Dashboard defaults to New tasks, and People defaults to the selected person. Plain-text/ordinary HTML paste keeps normal editor behavior. Input fields such as search, dates, and settings do not intercept whole-task payloads.

People clipboard history survives leaving and returning to the People tab in the same window.

Each paste creates fresh IDs and independent copies, including repeated pastes. The destination supplies the person/category and scheduling date; completion, source recurrence, send history, and source ownership are not copied. Recurrence remains off, including when pasting below a repeatable task. Source rich-text marks are retained.

A multi-task cut or paste creates one session Undo/Redo entry. Original rows are soft-deleted, so Undo restores their IDs and original metadata. Replacing clipboard contents does not remove the Undo entry. Multi-request failures retain undoable progress; an uncertain delete response is reconciled against the live task list on Undo. Editing a clipboard task in another window prevents a later destructive Undo/Redo from discarding the newer text. Conditional deletion also rejects stale revisions during Cut.

Attachments can be transferred between windows using the same Glance database. Their local URLs are rebased to the destination window's server port. Attachment payloads from another database are rejected before creating tasks; cross-installation attachment packaging is not implemented. Plain formatted tasks can cross databases. The scope identifier hashes the machine and database path and does not expose that path.

The normal session history and soft-delete retention rules still apply. The operation is a series of recoverable requests, not a database-wide atomic transaction. Clipboard write denial or save failure never starts deleting the selection.
