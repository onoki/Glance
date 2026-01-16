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
  "title": "Implement cybersecurity",
  "content": { ... },
  "position": 1234.5,
  "createdAt": 1710000000000,
  "updatedAt": 1710000005000,
  "completedAt": null
}

==================================================
CREATE TASK
==================================================

POST /api/tasks

REQUEST
{
  "page": "dashboard",
  "title": "Implement cybersecurity",
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
- title must be single-line and non-empty
- content must not contain heading nodes

==================================================
UPDATE TASK (TITLE AND/OR CONTENT)
==================================================

PUT /api/tasks/{taskId}

REQUEST
{
  "baseUpdatedAt": 1710000000000,
  "title": "Implement cybersecurity",
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
