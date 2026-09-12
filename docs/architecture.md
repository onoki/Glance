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
- optional checkbox or star markers in subcontent text (☐/☑/⭐)
- completion state
- optional scheduling and recurrence metadata

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
- Subcontent list items may include inline checkbox or star markers (☐/☑/⭐) as plain text
- Checkbox markers do not affect task completion
- Only tasks are reorderable and completable at the task level
- Tasks can be deleted when both title and subcontent are empty

### Keyboard behavior
- Enter at end of title indicates intent to create a new task
- Immediate Tab converts the new line into subcontent of the current task
- Tab inside subcontent indents/outdents

## Task action UI invariants (mandatory)

- In every task action row, the destructive delete action is the rightmost action.
- Space for contextual actions must be reserved so that showing them for the active or hovered task does not reflow task metadata, wrap dates, or make surrounding content jump.

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
- people, person_tags, person_tag_members
- task_send_events (small copy audit records; no target task link)
- status_update_runs (one retained revision per calendar day)

People tasks use the same completion window as Dashboard tasks: items completed since local midnight stay visible and can be unchecked; older completions are shown through History. Archived people remain separate from task completion and preserve their notes.

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
- Week starting YYYY-MM-DD
- No date
- Repeatable
- Notes

Completed tasks from earlier days are hidden from the dashboard.

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

### Status packages

- Status source documents live under `data/status-updates/YYYY-MM-DD/StatusSummary.json.gz`.
- A same-day recollection replaces the daily document and increments its revision.
- Excel and PowerPoint exports are generated from validated output and are not retained.
- Workplace readers are isolated under `server/Integrations` and disabled until configured.
- MSAL token cache files live under `data/auth` and are excluded from backups.

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

