# Weekday assignment

Implemented after user approval. This week and Next week are grouped Monday through Sunday.

- The task Move menu includes compact Mo–Su rows directly below This week and Next week, without separate headings. The Next week picker replaces the earlier column-only restriction, following the October 11, 2026 request. Tooltips show dates and the assigned date is marked. Past days are disabled; today and later days remain available. Moving preserves task text and supports Undo.
- This week remains visible even when empty. Today and remaining days have compact empty headers that accept task drops, including from New tasks. Past headers remain only while they have visible tasks. Existing unfinished tasks retain their scheduled dates; nothing is automatically postponed.
- Each current/upcoming header has a small + button that creates an empty task directly on that date and focuses its title. There are no large blank insertion areas for each day.
- Drops on a day are handled once and cannot fall through to the column's default-today scheduling. Tasks can still be reordered within a past day without changing their date.
- Empty current/upcoming days show a red insertion line while a whole task is dragged over them. The line occupies no layout space and clears on leaving, dropping or cancelling; text/image drags do not show it.
- The full space between weekday heading texts belongs to the preceding day, including CSS margins, flex gaps and the next header's leading blank space. Capture background drags/drops at the planning column and resolve the last heading above the pointer; never let these drops fall through to default-Monday scheduling. Task rows retain their own midpoint-based reorder targets. The visible spacing is unchanged.
- Next week remains visible when empty and displays full weekday names with dates. In two weeks is an optional column and Move choice after Next week: it assigns Monday of the week after next, then naturally rolls into Next week and This week without changing the stored date. It has no day-picker row.

Enter-created adjacent tasks, middle-title splits, and promoted subcontent inherit the source task's scheduled date, including when editing an older task. This does not change the restriction on moving tasks to past days in the picker.

Regression coverage: categoryUtils.test.js (Wednesday, weekends and rollover), weekdayDrag.test.js (empty targets/New tasks and expired dates), useDashboardData.test.js (dated creation, persisted moves and Undo/Redo). Shared editing and category-move tests remain applicable.
