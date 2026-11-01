# Architecture

## Purpose

This document defines the **authoritative architecture** of the application.
All implementation must conform to this document unless explicitly changed.

This application is a **local-first desktop task and note application**
with rich text editing, project management aspects, and file-based persistence.

---

## Technology stack

- Desktop host: Photino.NET
- Backend: .NET (ASP.NET Core, local-only HTTP)
- Frontend UI: Vue
- Rich text editor: Tiptap (ProseMirror)
- Database: SQLite (WAL mode)
- Search: SQLite FTS5
- Storage: file-based (copyable folder)

No admin or root permissions are required.

---

## High-level architecture

The application consists of a **single OS process** per instance:

- Photino window hosting the UI
- Embedded ASP.NET Core server (localhost only)
- SQLite database and filesystem blobs
  - HTML UI responses are served with no-store headers to avoid stale caches after updates

Multiple instances may run simultaneously and share the same data directory.

---

## Data ownership

- The backend is the **only component** that:
  - writes to SQLite
  - writes to the filesystem
- The UI communicates exclusively via HTTP / WebSocket.
- UI state is disposable and may be recreated at any time.

---

## Task model (core domain)

A **task** is the atomic unit of organization.

Each task consists of:
- a formatted title (may be empty)
- structured rich-text subcontent (optional)
- optional checkbox, star, or question markers in subcontent text (☐/☑/⭐/❓)
- completion state
- optional scheduling and recurrence metadata
- a durable private-subcontent flag (screen presentation only)
- a separate history archive timestamp, independent of completion time

Subtasks exist only as **textual structure inside a task**.
They are not independently movable and do not become tasks when checked.

---

## Editor invariants (mandatory)

The editor must enforce:

- Exactly one title node per task
- Title:
  - formatted text (same marks as subcontent)
  - line breaks allowed
- Subcontent:
  - may contain nested lists
  - may contain bold, italic, and highlight (green/yellow/red)
  - may contain links and images
- Link marks may target http(s), mail addresses, absolute mapped-drive paths, UNC paths, and file URIs.
- Web URLs are detected automatically and Ctrl+K creates or edits a link. File links are opened only through the desktop bridge after a second native validation; arbitrary URL schemes and executable/script targets are rejected.
- Subcontent list items may include inline checkbox, star, or question markers (☐/☑/⭐/❓) as plain text
- Long web URLs may be shortened visually while unfocused; rich-text source, clipboard text, and hyperlink destinations remain unchanged. Typing at a link boundary inserts unlinked text. Document capture handlers snapshot the activated destination before mousedown can focus/reflow the editor, cancel native click/auxclick/anchor-Enter navigation, open through the external bridge, and only then focus the original editor. Background URL/file drops cannot replace the app; editor drop handling remains separate. Links also render with target=_blank and rel=noopener noreferrer as a fallback.
- Checkbox markers do not affect task completion
- Only tasks are reorderable and completable at the task level
- Tasks can be deleted when both title and subcontent are empty

### Keyboard behavior
- Enter at end of title indicates intent to create a new task
- Immediate Tab converts the new line into subcontent of the current task
- Tab inside subcontent indents/outdents
- Enter in the middle of a title still splits it into a separate task, carrying existing subcontent (unchanged by the September 2026 usability revision).
- Dashboard and People share editor keyboard behavior, including focus requests applied when editors first mount.
- Ctrl+3 toggles a question marker; highlights use Ctrl+4/5/6. Ctrl+7 is unassigned after retirement of status reporting.

## Task action UI invariants (mandatory)

- In every task action row, the destructive delete action is the rightmost action.
- Contextual actions and dates appear together at the right above the focused task. Only individual buttons and labels have opaque backgrounds; unused overlay space is transparent and passes clicks through. They reserve no task-row height and may cover previous lines, but must never move or reflow tasks. Their position is constrained to the visible horizontal portion of the list and viewport.
- All Dashboard categories, including New tasks, support mouse resizing. Rich text wraps unbroken strings; category contents stay scrolled to the left while the dashboard itself can scroll horizontally.
- People navigation wraps and displays stable colored tag dots with tooltips and a shared legend, without per-person text badges or tag filters.

---

## Persistence

### SQLite
- WAL mode enabled
- Short transactions
- One database file

### Core tables
- tasks
- task_search (FTS5)
- changes
- app_meta (app metadata such as window size)
  - `window_placement` stores last successfully closed normal bounds and monitor identity as JSON; `window_size` remains compatible with older versions. Startup restores normal bounds even after a maximized/minimized close, clamps to the current monitor work area, and uses the primary monitor if the saved device is absent. These values are covered by the existing app_meta portable export contract.
- people, person_tags, person_tag_members
- task_send_events (small copy audit records; no target task link)
- status_update_runs (one retained revision per calendar day)

People tasks use the same completion window as Dashboard tasks: items completed since local midnight stay visible until explicitly archived and can be unchecked; older completions are shown through History. `history_archived_at` removes a completion from active lists without rewriting `completed_at`. The History chart counts all nondeleted completed tasks by local completion day, including today’s unarchived completions, over 180 days. Manual archiving includes today’s Dashboard and People notes; automatic archiving includes only prior-day completions. Both are transactional and idempotent. Unchecking clears completion and archive timestamps. Local midnight calculation uses the timezone offset at midnight, including DST changes. A midnight timer and focus/visibility recovery share one in-flight day check; saves settle first, and the day checkpoint advances only after successful archive and view refresh. Archived people remain separate from task completion and preserve their notes.

`usePrivacy` owns ephemeral per-window presentation state and starts protected on each launch. The shared TaskItem keeps the body mounted at its original height but invisible, inert and aria-hidden when a flagged task is not focused; the title and a reveal placeholder remain visible. Task/window blur conceals it, and native window focus restoration alone cannot reveal it. The top-bar lock toggles this presentation for all flagged tasks in that window. `is_private` is preserved through saves, sends, recurrence, clipboard and structural history; joins OR the source flags and splits inherit them. Search still indexes full text. This is not encryption; backups, clipboard and machine-readable exports retain all data. Offline HTML exports use collapsed Private subcontent disclosures. See [privacy and history behavior](privacy-and-history.md).

### Attachments
- Stored as files under a blobs directory
- Referenced by ID in task content

---

## Search

- All text (title + subcontent) is indexed
- Search returns tasks
- Results are read-only
- Matching text is highlighted in the UI
- Each result can open its source task in Dashboard, People, or History and return to the retained search. An open note owned by an archived person routes to the Archived people view until the person is restored.

---

## Derived categories (dashboard)

Task categories are **derived**, not stored:

- New
- Uncategorized
- This week and Next week (always available, grouped by weekday)
- In two weeks (when populated, after Next week)
- Week starting YYYY-MM-DD
- No date
- Repeatable
- Notes

Completed tasks from earlier days are hidden from the dashboard.

Move offers compact weekday choices below both This week and Next week. In two weeks assigns Monday of the week after next; it uses the same scheduled-date field as other dated work. Its column becomes Next week and then This week as the calendar advances, without rewriting dates or adding a stored category. Other dated weeks retain their Week starting label.

---

## Multiple-view and save behavior

- A single Glance desktop process owns one ASP.NET server and one SQLite database, and can open multiple native Photino windows.
- Each window has independent navigation state and polls the shared `changes` table; clean notes reflect external changes within about one second.
- Task text and status-marker writes use atomic optimistic concurrency (`updated_at = baseUpdatedAt`). A stale editor receives HTTP 409 and its local text remains visible; Glance never silently overwrites the newer stored note.
- Every editable task registers with a shared per-window save coordinator. Saves are serialized per task and use edit generations, so text typed during an earlier request remains dirty and is saved next.
- Native close is a handshake: the desktop cancels the first close request, asks every affected webview to flush, and closes only after all acknowledge success. A failed save keeps the window open.
- Backup, portable export, update, and restore actions request a flush from every open Glance window before taking their snapshot or changing application state.
- `beforeunload` is only a warning fallback in an ordinary development browser; browsers cannot reliably await asynchronous saves during teardown.

---

## Folder layout

glance/
├─ ui/
├─ server/
├─ docs/
    ├─ schema.sql
    └─ migrations/
        ├─ 001_init.sql
        ├─ 002_add_title_json.sql
        ├─ 003_add_app_meta.sql
├─ data/
│  ├─ glance.db
│  ├─ glance.db-wal
│  └─ glance.db-shm
└─ blobs/
   └─ attachments/

- `ui/` contains the Vue frontend.
- `server/` contains the ASP.NET Core backend.
- `docs/` contains authoritative documentation.
- `data/` contains the SQLite database files.
- `blobs/attachments/` contains file-based attachments.

Copying the `glance/` directory is a valid backup.

### Data portability invariant

- `GET /api/export/portable` produces a versioned, neutral exit bundle with lossless JSON, offline HTML, status JSON, media, and a hashed manifest.
- Every durable table and column must be included in the bundle, or explicitly classified as derived/ephemeral.
- A migration or durable feature is incomplete until its export classification, versioned schema, human-readable rendering where relevant, and coverage tests are updated.
- Export runs against a consistent SQLite snapshot and refuses unknown durable fields instead of silently omitting them.
- Rich-text link marks and file-link targets are durable note data and must survive export.

See `docs/data-portability.md` and `schema/glance-export-v1.schema.json`.

### Legacy status data

Status reporting was retired on October 19, 2026. Its UI, marker actions, collection/import/download endpoints, workplace readers, MSAL integration and Office exporters have been removed. No database columns or saved reports are deleted. The rich-document metadata serializer, derived legacy timestamp compatibility, schema validation for old compressed reports, backup/restore and neutral portable export remain so old data stays recoverable. JsonSchema.Net remains required for validating legacy backup contents; Office and MSAL dependencies are removed. Existing auth caches are not used and remain excluded from backups.

---

## Non-goals (explicit)

- Real-time collaborative editing
- CRDT-based merging
- Cloud sync (may be added later)

---

## Authority

- `architecture.md` is normative
- `requirements.feature` defines behavioral requirements
- `api.md` defines external contracts

Implementations must not modify these documents unless explicitly instructed.


## Usability refinements

- Outermost Shift+Tab carries the ProseMirror selection through the split payload into the promoted title in both views. Schema-equivalent server content does not replace the editor document, preventing selection resets from JSON property order or default attributes.
- Native Windows bounds are reapplied after window creation using signed desktop coordinates and the current monitor work area. `data/desktop.log` records saved, requested, and actual bounds to diagnose initialization or monitor/DPI differences. The last successfully closed window still wins.
- People tag legend dots and labels share a centered inline-flex row. Add Person uses the common app font at regular weight.
- The current button inventory and proposed shared variants are in [button-styles.md](button-styles.md); the accepted shared control system is implemented in `ui/src/styles/controls.css`.

- IBM Plex Sans is the only bundled font family, with upright and italic variable faces (weights 100–700), attribution and SIL OFL 1.1 license in `ui/assets/fonts`. All tabs use the shared 7px tokens, 1.35 line height and neutral Notepad palette. Genuine task bold/italic replaces synthetic formatting; controls remain regular. See `ui/AGENTS.md` for mandatory typography rules. The Settings license URL is imported as a Vite asset so attribution is available in portable builds.
- Successful task deletion calls `saveCoordinator.forget` before removing the row. Retired save operations cannot become orphaned close blockers. Normal unmount still flushes edits.
- People tag colors use `app_meta` keys `tag_color:<tag-id>` with validated six-digit hex values. They are returned with tags, preserved by the existing metadata export contract, and removed with the tag.
- The selected person is stored per window in sessionStorage and validated against the active directory when People mounts.
- Immediate Enter/Tab consumes the pending creation intent and appends an empty subitem. If creation is in flight, Tab targets the returned new task ID, never the original row.
- Whole-task clipboard behavior is documented in [task-clipboard-proposal.md](task-clipboard-proposal.md).

## Whole-task clipboard implementation

`taskClipboard` registers editable Dashboard/People task rows and owns whole-task selection and browser clipboard events. Task text stays in the editor's ordinary clipboard path unless a whole-task payload is present. The HTML payload is versioned and validated, with size/depth limits and permitted rich-text nodes/marks. `taskClipboardAdapter` captures destination metadata and insertion positions; `taskClipboardHistory` supplies grouped Undo/Redo callbacks to each view's existing history stack.

Cut awaits the system clipboard write and the save coordinator before soft deletion. Delete requests may include `baseUpdatedAt`; revision checks occur inside the deletion transaction, preserving both task and search state on conflict. Uncertain deletion responses retain history intent. Successful deletions retire save registrations. Incomplete group operations retain progress for recovery rather than claiming atomicity.

`GET /api/clipboard-scope` hashes the machine/database path to identify shared attachment storage across local server ports. Attachment-bearing payloads require that same scope, and attachment URLs are rebased on paste. No new durable table or export schema is introduced.

Window taskbar grouping uses Glance.Desktop on the process, portable shortcut and each native window. Window-created handlers assign the explicit window property, and successful close clears it before destruction. This follows the Windows per-window AppUserModelID contract; native property roundtrips are tested without changing Windows taskbar settings.


## Shared compact presentation

The IBM Plex Sans / Notepad comparison is the visual reference. `compact.css` holds font/palette tokens, `app.css` retains layout, `controls.css` shares command geometry and behavior, and `surfaces.css` applies visual hierarchy across Dashboard, People, Search, History and Settings. Headers gain genuine semibold and sentence case; ordinary/selected controls stay regular to prevent selection-dependent width changes. Small radii, pale selected fills and thin separators supply visual structure without more whitespace. The 2px task ownership rail is a pseudo-element, with the existing 1px full-width toolbar line; neither changes layout or hit testing. A 10px visible checkbox remains inside its 12px target. Shared task padding/indentation also applies to read-only views. See [compact visual system](button-styles.md) for the current tokens and invariants.
