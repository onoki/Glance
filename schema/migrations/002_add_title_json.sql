PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;

ALTER TABLE tasks ADD COLUMN title_json TEXT NULL;
