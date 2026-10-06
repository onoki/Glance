# Repeated Tab merge regression

The regression test reproduces a single-window conflict using mounted TaskItem save owners and the real Dashboard/People composables against a strict revision-checking API. Before the fix, a queued autosave and direct Tab merge could write with stale revisions. Dashboard's save refresh also retained a dirty editor snapshot containing the pre-save revision, even after the server had acknowledged its write.

The fix settles pending saves before capturing merge documents, carries acknowledged revisions into retained dirty snapshots, and serializes Tab requests and their Undo/Redo history across the list. Current source rows are resolved again after the wait. Genuine external conflicts still block restructuring and preserve unsaved local text.

Blank-line filtering was an older requirement, explicitly replaced by the user's October 6 feedback. Tab now appends the entire source title and subcontent without trimming either document. The rapid Enter/Tab path and Shift+Tab promotion also preserve blank siblings. Editor blur no longer silently clears a blank-only list.

Adjacent findings: Dashboard Tab used global task ordering, potentially selecting another column's task. It now shares the visible-neighbour ordering used by arrow navigation and Backspace. Its old hidden-source approach also made Undo treat a live hidden row as missing and recreate it; recoverable deletion now matches the People path and keeps the source identity through Undo/Redo.

Verification includes ten consecutive/overlapping Tab merges per view, pending and in-flight autosaves, rich/nested/image/blank-only content combinations, repeated keystrokes, column boundaries, Shift+Tab sibling preservation, immediate Undo during pending merges, Redo and real external conflicts. Browser checks use disposable server data and six empty tasks per view, with a pending title edit in People. Both views retain all nine expected subcontent rows and their saved results after navigation/reload.
