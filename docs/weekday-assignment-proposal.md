# Weekday assignment

Implemented after user approval. This week remains grouped Monday through Sunday.

- The task Move menu includes Mon–Sun buttons with full dates in tooltips and the assigned date marked. Past days are disabled; today and later days remain available. Moving preserves task text and supports Undo.
- This week remains visible even when empty. Today and remaining days have compact empty headers that accept task drops, including from New tasks. Past headers remain only while they have visible tasks. Existing unfinished tasks retain their scheduled dates; nothing is automatically postponed.
- Each current/upcoming header has a small + button that creates an empty task directly on that date and focuses its title. There are no large blank insertion areas for each day.
- Drops on a day are handled once and cannot fall through to the column's default-today scheduling. Tasks can still be reordered within a past day without changing their date.

Regression coverage: categoryUtils.test.js (Wednesday, weekends and rollover), weekdayDrag.test.js (empty targets/New tasks and expired dates), useDashboardData.test.js (dated creation, persisted moves and Undo/Redo). Shared editing and category-move tests remain applicable.
