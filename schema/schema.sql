-- Baseline schema for NEW installs.
-- For upgrades, the application must apply SQL files from docs/migrations in ascending order.
-- All timestamps are UTC milliseconds since epoch.

PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;

------------------------------------------------------------
-- Schema migrations (authoritative upgrade mechanism)
------------------------------------------------------------
CREATE TABLE IF NOT EXISTS schema_migrations (
  version INTEGER PRIMARY KEY,
  applied_at INTEGER NOT NULL
);

------------------------------------------------------------
-- Tasks (atomic unit)
------------------------------------------------------------
CREATE TABLE IF NOT EXISTS tasks (
  id TEXT PRIMARY KEY,
  page TEXT NOT NULL,               -- e.g. 'dashboard'
  title TEXT NOT NULL,              -- plain text title (derived)
  title_json TEXT NOT NULL,         -- rich title (JSON)
  content_json TEXT NOT NULL,       -- rich subcontent (JSON)
  position REAL NOT NULL,           -- ordering within derived category
  created_at INTEGER NOT NULL,      -- UTC ms
  updated_at INTEGER NOT NULL,      -- UTC ms
  completed_at INTEGER NULL,        -- UTC ms
  scheduled_date TEXT NULL,         -- YYYY-MM-DD (derived categories)
  recurrence_json TEXT NULL,        -- recurrence config (JSON)
  owner_person_id TEXT NULL REFERENCES people(id) ON DELETE SET NULL,
  status_input_at INTEGER NULL,      -- earliest active Ctrl+6 marker, UTC ms
  origin_label TEXT NULL,            -- informational provenance for independent copies
  send_marker_dismissed_at INTEGER NULL,
  deleted_at INTEGER NULL            -- soft deletion time; purged after the recovery window
);

------------------------------------------------------------
-- People and user-defined groups
------------------------------------------------------------
CREATE TABLE IF NOT EXISTS people (
  id TEXT PRIMARY KEY,
  display_name TEXT NOT NULL,
  position REAL NOT NULL,
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL,
  archived_at INTEGER NULL
);

CREATE TABLE IF NOT EXISTS person_tags (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL COLLATE NOCASE UNIQUE,
  position REAL NOT NULL,
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS person_tag_members (
  person_id TEXT NOT NULL REFERENCES people(id) ON DELETE CASCADE,
  tag_id TEXT NOT NULL REFERENCES person_tags(id) ON DELETE CASCADE,
  PRIMARY KEY (person_id, tag_id)
);

CREATE TABLE IF NOT EXISTS task_send_events (
  id TEXT PRIMARY KEY,
  source_task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
  destination_kind TEXT NOT NULL,
  destination_id TEXT NULL,
  destination_label TEXT NOT NULL,
  direction TEXT NOT NULL,
  sent_at INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS status_update_runs (
  report_date TEXT PRIMARY KEY,
  report_id TEXT NOT NULL,
  input_revision INTEGER NOT NULL,
  document_status TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL,
  completed_at INTEGER NULL,
  input_sha256 TEXT NOT NULL,
  relative_json_path TEXT NOT NULL
);

------------------------------------------------------------
-- Full-text search (title + all subcontent text)
------------------------------------------------------------
CREATE VIRTUAL TABLE IF NOT EXISTS task_search
USING fts5(
  task_id,
  content,
  tokenize = 'unicode61'
);

------------------------------------------------------------
-- Change log (multi-instance consistency)
------------------------------------------------------------
CREATE TABLE IF NOT EXISTS changes (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  entity_type TEXT NOT NULL,        -- 'task'
  entity_id TEXT NOT NULL,
  change_type TEXT NOT NULL,        -- 'create', 'update', 'complete'
  changed_at INTEGER NOT NULL       -- UTC ms
);

------------------------------------------------------------
-- App metadata (observability/version tracking)
------------------------------------------------------------
CREATE TABLE IF NOT EXISTS app_meta (
  key TEXT PRIMARY KEY,
  value TEXT NOT NULL
);

------------------------------------------------------------
-- Indices
------------------------------------------------------------
CREATE INDEX IF NOT EXISTS idx_tasks_page_position
ON tasks (page, position);

CREATE INDEX IF NOT EXISTS idx_tasks_completed_at
ON tasks (completed_at);

CREATE INDEX IF NOT EXISTS idx_people_archived_position
ON people (archived_at, position);

CREATE INDEX IF NOT EXISTS idx_tasks_owner_page_position
ON tasks (owner_person_id, page, position);

CREATE INDEX IF NOT EXISTS idx_tasks_status_input_at
ON tasks (status_input_at);

CREATE INDEX IF NOT EXISTS idx_tasks_deleted_at
ON tasks (deleted_at);

CREATE INDEX IF NOT EXISTS idx_task_send_events_source_sent
ON task_send_events (source_task_id, sent_at DESC);

------------------------------------------------------------
-- Record baseline migration as version 1 (for fresh installs)
------------------------------------------------------------
INSERT OR IGNORE INTO schema_migrations(version, applied_at)
VALUES (5, CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER));
