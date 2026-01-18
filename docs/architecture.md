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
- a non-deletable title (formatted text)
- structured rich-text subcontent
- completion state
- optional scheduling and recurrence metadata

Subtasks exist only as **textual structure inside a task**.
They are not independently completable or movable.

---

## Editor invariants (mandatory)

The editor must enforce:

- Exactly one title node per task
- Title:
  - formatted text (same marks as subcontent)
  - line breaks allowed
  - cannot be deleted
- Subcontent:
  - may contain nested lists
  - may contain bold, italic, and highlight (green/yellow/red)
  - may contain links and images
- Subcontent has no independent completion state
- Only tasks are reorderable and completable

### Keyboard behavior
- Enter at end of title indicates intent to create a new task
- Immediate Tab converts the new line into subcontent of the current task
- Tab inside subcontent indents/outdents

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

### Attachments
- Stored as files under a blobs directory
- Referenced by ID in task content

---

## Search

- All text (title + subcontent) is indexed
- Search returns tasks
- Results are read-only
- Matching text is highlighted in the UI

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

## Multi-instance behavior

- Multiple application instances may run concurrently
- All instances share the same SQLite database
- Consistency model:
  - last-write-wins
  - no locking between instances
- Instances detect changes by polling the `changes` table
- UI updates reflect external changes within ~1 second

---

## Folder layout

glance/
├─ ui/
├─ server/
├─ docs/
    ├─ schema.sql
    └─ migrations/
        ├─ 001_init.sql
        ├─ 002_add_task_color.sql
        ├─ 003_add_task_archived.sql
        └─ 004_rebuild_fts.sql
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
