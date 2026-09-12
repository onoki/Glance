API CONTRACT (v1)

This document defines the authoritative HTTP API between the UI and backend.

- Local-only (127.0.0.1)
- JSON over HTTP
- UTF-8 encoding
- All timestamps are UTC milliseconds since epoch
- All IDs are opaque strings (UUID)

==================================================
TASK MODEL (API REPRESENTATION)
==================================================

{
  "id": "uuid",
  "page": "dashboard",
  "title": {
    "type": "doc",
    "content": [
      {
        "type": "paragraph",
        "content": [{ "type": "text", "text": "Implement cybersecurity" }]
      }
    ]
  },
  "content": { ... },
  "position": 1234.5,
  "createdAt": 1710000000000,
  "updatedAt": 1710000005000,
  "completedAt": null
}

NOTES
- content may include inline checkbox or star markers (☐/☑/⭐) as plain text

==================================================
CREATE TASK
==================================================

POST /api/tasks

REQUEST
{
  "page": "dashboard",
  "title": {
    "type": "doc",
    "content": [
      {
        "type": "paragraph",
        "content": [{ "type": "text", "text": "Implement cybersecurity" }]
      }
    ]
  },
  "content": {
    "type": "doc",
    "content": []
  },
  "position": 1000
}

RESPONSE
{
  "taskId": "uuid",
  "updatedAt": 1710000000000
}

VALIDATION
- title must be a ProseMirror doc node without list or heading nodes
- content must not contain heading nodes

==================================================
UPDATE TASK (TITLE AND/OR CONTENT)
==================================================

PUT /api/tasks/{taskId}

REQUEST
{
  "baseUpdatedAt": 1710000000000,
  "title": {
    "type": "doc",
    "content": [
      {
        "type": "paragraph",
        "content": [{ "type": "text", "text": "Implement cybersecurity" }]
      }
    ]
  },
  "content": {
    "type": "doc",
    "content": [ ... ]
  }
}

RESPONSE
{
  "updatedAt": 1710000005000,
  "externalUpdate": false
}

BEHAVIOR
- The update is atomic and succeeds only when baseUpdatedAt equals the stored updatedAt.
- A stale write returns HTTP 409 with `{ "error": "Conflict", "currentUpdatedAt": ... }` and does not change the stored note.
- A successful updatedAt is strictly greater than the prior value, including edits within the same millisecond.
- `externalUpdate` remains false in successful responses for backwards-compatible response shape.
- Server updates:
  - tasks table
  - task_search FTS index
  - changes table

==================================================
COMPLETE / UNCOMPLETE TASK
==================================================

POST /api/tasks/{taskId}/complete

REQUEST
{
  "completed": true
}

RESPONSE
{
  "completedAt": 1710000100000
}

RULES
- Applies only to the task (not subcontent)
- Setting completed=false clears completedAt

==================================================
DASHBOARD QUERY
==================================================

GET /api/dashboard

RESPONSE
{
  "newTasks": [ Task ],
  "mainTasks": [ Task ]
}

RULES (ITERATION 1)
- Category derivation may be simplified
- Completed tasks may be filtered out

==================================================
SEARCH
==================================================

GET /api/search?q=term

RESPONSE
{
  "query": "term",
  "results": [
    {
      "task": Task,
      "matches": ["term"]
    }
  ]
}

RULES
- All task text (title + subcontent) is searchable
- Results are read-only
- UI highlights matching text

==================================================
CHANGE POLLING (MULTI-INSTANCE)
==================================================

GET /api/changes?since={lastId}

RESPONSE
{
  "lastId": 130,
  "changes": [
    {
      "entityType": "task",
      "entityId": "uuid",
      "changeType": "update",
      "changedAt": 1710000200000
    }
  ]
}

RULES
- Clients poll approximately every 750 ms
- Server returns changes with id > since
- Clients reload affected tasks

==================================================
APP UPDATE (LOCAL PACKAGE)
==================================================

POST /api/update

REQUEST (multipart/form-data)
- field name: package
- file type: .zip

RESPONSE
{
  "ok": true,
  "version": "2026-01-24 17:02",
  "message": "Update staged. Restarting now..."
}

RULES
- Local-only endpoint
- The ZIP must include a glance.update.json manifest
- The manifest defines version, algorithm, hash, and format
- Server validates the hash against extracted package contents
- Update version must be newer than the currently running version

==================================================
ERRORS
==================================================

Standard HTTP status codes are used.

ERROR RESPONSE EXAMPLE
{
  "error": "ValidationError",
  "message": "Task title must be a single line"
}

==================================================
BACKWARDS COMPATIBILITY
==================================================

- Fields may be added but not removed
- Existing endpoint semantics must not change
- Breaking changes require a new API version

==================================================
PEOPLE AND TASK COPIES
==================================================

- `GET/POST /api/people`; `PUT /api/people/{id}` (rename/archive/restore)
- `PUT /api/people/{id}/tags`; CRUD under `/api/people/tags`
- `GET /api/people/{id}/tasks`
- `POST /api/tasks/{id}/send-to-people` with `personIds` and/or `tagIds`
- `POST /api/tasks/{id}/send-to-dashboard`
- `GET /api/tasks/{id}/send-events`
- `POST /api/tasks/{id}/send-marker/dismiss`

Sends create independent copies and retain only a small source-side event. Dismissing `↗` hides the marker without deleting its event history.

==================================================
STATUS UPDATES
==================================================

- `PUT /api/tasks/{id}/status-markers` persists structured status metadata shown as `📝` in the UI.
- `GET /api/status-updates` returns configuration and latest daily-run state.
- `POST /api/status-updates/authenticate` starts/tests the configured Microsoft sign-in without exposing tokens.
- `POST /api/status-updates/collect` creates/replaces today's input revision.
- `GET /api/status-updates/json` and `/schema` download the handoff files.
- `POST /api/status-updates/import` accepts multipart field `summary` and strictly validates it.
- `GET /api/status-updates/excel` and `/powerpoint` generate Office output after a valid completed import.

Status collection/import endpoints that mutate retained packages are local-only.

==================================================
VERIFIED BACKUPS AND RESTORE
==================================================

- `GET /api/backups` lists real restore points by internal backup ID, including timestamps,
  verification state, content counts, reason, size, and local/second-location availability.
- `POST /api/backups` creates a new verified snapshot. The desktop UI flushes every open window
  before calling this endpoint.
- `POST /api/backups/{backupId}/verify` rechecks the selected ZIP, manifest hashes, archived
  SQLite database, rich-note JSON, attachments, and retained status JSON.
- `GET/PUT /api/data-safety/settings` reads or changes hourly-backup and optional second-location
  settings.
- `POST /api/data-safety/test-location` accepts `{ "location": "..." }` and reports whether the
  directory is currently available and writable.
- `GET /api/data-safety/startup` reports normal or recovery-mode startup state and whether a
  verified restore is currently staged in `pendingRestore`.
- `POST /api/data-safety/restore` accepts `{ "backupId": "..." }`, verifies and stages that exact
  restore point, and returns `restartRequired: true` after creating a verified emergency backup.

RULES
- A restore is selected from the backup catalog; arbitrary filenames and filesystem paths are
  not accepted.
- The staged state is installed before migrations on the next start and checked again. Failure
  restores the pre-restore rescue state and quarantines the failed plan instead of retrying it.
- In recovery mode, note-mutating APIs are blocked while backup, verification, restore, and
  diagnostic endpoints remain available.

==================================================
PORTABLE EXPORT
==================================================

GET /api/export/portable

RESPONSE
- `application/zip`
- filename `GlanceExport-v1-<timestamp>.zip`

RULES
- Local-only endpoint
- Reads a consistent SQLite snapshot
- Contains lossless JSON, readable HTML, media, status JSON, and a SHA-256 manifest
- Returns HTTP 409 if a durable database table or column has not been classified for export
- The versioned JSON contract is `schema/glance-export-v1.schema.json`
