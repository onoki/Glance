PRAGMA foreign_keys = ON;

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

ALTER TABLE tasks ADD COLUMN owner_person_id TEXT NULL REFERENCES people(id) ON DELETE SET NULL;
ALTER TABLE tasks ADD COLUMN status_input_at INTEGER NULL;
ALTER TABLE tasks ADD COLUMN origin_label TEXT NULL;
ALTER TABLE tasks ADD COLUMN send_marker_dismissed_at INTEGER NULL;

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

CREATE INDEX IF NOT EXISTS idx_people_archived_position
ON people (archived_at, position);

CREATE INDEX IF NOT EXISTS idx_tasks_owner_page_position
ON tasks (owner_person_id, page, position);

CREATE INDEX IF NOT EXISTS idx_tasks_status_input_at
ON tasks (status_input_at);

CREATE INDEX IF NOT EXISTS idx_task_send_events_source_sent
ON task_send_events (source_task_id, sent_at DESC);

