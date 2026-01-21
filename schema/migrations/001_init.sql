PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;

CREATE TABLE IF NOT EXISTS schema_migrations (
  version INTEGER PRIMARY KEY,
  applied_at INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS tasks (
  id TEXT PRIMARY KEY,
  page TEXT NOT NULL,
  title TEXT NOT NULL,
  content_json TEXT NOT NULL,
  position REAL NOT NULL,
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL,
  completed_at INTEGER NULL,
  scheduled_date TEXT NULL,
  recurrence_json TEXT NULL
);

CREATE VIRTUAL TABLE IF NOT EXISTS task_search
USING fts5(
  task_id,
  content,
  tokenize = 'unicode61'
);

CREATE TABLE IF NOT EXISTS changes (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  entity_type TEXT NOT NULL,
  entity_id TEXT NOT NULL,
  change_type TEXT NOT NULL,
  changed_at INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_tasks_page_position
ON tasks (page, position);

CREATE INDEX IF NOT EXISTS idx_tasks_completed_at
ON tasks (completed_at);

INSERT OR IGNORE INTO schema_migrations(version, applied_at)
VALUES (1, CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER));
