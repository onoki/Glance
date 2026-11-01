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
  "completedAt": null,
  "historyArchivedAt": null,
  "isPrivate": false
}

NOTES
- content may include inline checkbox or star markers (☐/☑/⭐) as plain text
- isPrivate defaults to false on creation; an omitted isPrivate on update preserves the saved flag
- privacy conceals subcontent in the UI; the local API still returns the complete documents
- historyArchivedAt is server-managed and never substitutes for completedAt

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
- Setting completed=false clears completedAt and historyArchivedAt
- Setting completed=true on an already completed task retains its original completion timestamp and archive state

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
- `POST /api/tasks/{id}/send-to-people` with `personIds`, `tagIds`, and optional `includeDashboard` (default false). People sources may select Dashboard alongside people/tag recipients in one transaction. Dashboard sources ignore includeDashboard.
- `POST /api/tasks/{id}/send-to-dashboard`
- `GET /api/tasks/{id}/send-events`
- `POST /api/tasks/{id}/send-marker/dismiss`

Sends create independent copies, preserve isPrivate, and retain only a small source-side event. Dismissing `↗` hides the marker without deleting its event history.

==================================================
RETIRED STATUS UPDATES
==================================================

The Status Updates endpoints and marker controls have been removed. Previously saved database and report data remain supported by backup/restore/export.

==================================================
HISTORY ARCHIVING
==================================================

- `POST /api/history/move-completed-to-history` archives nondeleted Dashboard/People completions since local midnight.
- `POST /api/history/archive-previous-days` archives nondeleted Dashboard/People completions before local midnight.
- Both endpoints require a loopback request and return `{ "moved": count }`.
- Each archive transaction sets history_archived_at once, advances the revision, and emits an archive change per task. Repeated calls return zero for already archived tasks.
- Neither endpoint alters completed_at, content, privacy, ownership or scheduled dates. Open/deleted tasks are untouched.
- History lists and activity include all completed, nondeleted tasks. Activity groups by completed_at in the current local timezone, regardless of archive status.
