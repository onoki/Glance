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
  recurrence_json TEXT NULL         -- recurrence config (JSON)
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
-- Indices
------------------------------------------------------------
CREATE INDEX IF NOT EXISTS idx_tasks_page_position
ON tasks (page, position);

CREATE INDEX IF NOT EXISTS idx_tasks_completed_at
ON tasks (completed_at);

------------------------------------------------------------
-- Record baseline migration as version 1 (for fresh installs)
------------------------------------------------------------
INSERT OR IGNORE INTO schema_migrations(version, applied_at)
VALUES (2, CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER));
