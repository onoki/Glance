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
- If baseUpdatedAt < current updatedAt:
  - update still succeeds
  - externalUpdate = true
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
