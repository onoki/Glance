# Iteration 2 Notes

Added:
- Task editing moved into per-task components with debounced autosave and blur save.
- Draft input row for New tasks creates tasks when typing a title.
- Dirty edit tracking to avoid overwriting local edits during change polling.

Behavior:
- Tasks sync via existing API endpoints and continue to poll /api/changes.
- External changes refresh lists, but local dirty tasks keep their edits.
