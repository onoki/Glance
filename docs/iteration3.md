# Iteration 3 Notes

Added:
- Tiptap-based rich text editor for task subcontent with bold, italic, and multicolor highlights.
- Title keyboard behaviors: Enter creates a task below; Tab moves focus to subcontent and cancels pending creation.
- Dirty state badge and sync guard to avoid overwriting active edits during change polling.

Known limitations:
- No images, paste handling, or advanced formatting yet.
- No search UI highlighting or recurrence logic.
