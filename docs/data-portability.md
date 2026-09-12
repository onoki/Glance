# Data portability

Glance provides a neutral exit bundle so notes remain usable without Glance and can be converted to another notes system later.

Use **Settings → Export all notes**. Glance downloads a ZIP named `GlanceExport-v1-<timestamp>.zip` containing:

- `index.html`, `dashboard.html`, `people.html`, and `history.html` for offline reading in a browser;
- `data/glance.json`, the lossless machine-readable record;
- `media/attachments/`, containing note images;
- `status-updates/`, containing uncompressed status-report JSON; and
- `manifest.json`, containing the SHA-256 hash and byte length of every payload file.

The machine-readable record retains rich title and content JSON (including hyperlink marks and file targets), ordering, recurrence, completion and deletion timestamps, archived people, user-defined tags and memberships, send events, status metadata, and schema/app/export versions. Full-text search and the short-lived change log are explicitly classified as derived data because they can be rebuilt from task records.

The version-1 JSON contract is [glance-export-v1.schema.json](../schema/glance-export-v1.schema.json). A future importer should verify the ZIP paths, manifest hashes and schema before consuming a bundle.

## Required maintenance rule

Every new durable feature must be represented by the portable export or explicitly classified as derived/ephemeral. A schema migration that adds a durable table or column must update all of the following in the same change:

1. `PortableExportCoverage`;
2. the versioned JSON schema;
3. human-readable HTML rendering when the field has user-visible meaning; and
4. portability tests and this document when behavior changes.

Glance intentionally refuses to export an unclassified durable table or column. This makes an incomplete exit path visible during development instead of silently losing newer data.

The bundle is designed as the stable source for a future OneNote-specific converter. The HTML files can already be copied manually into OneNote, while a programmatic converter can read `data/glance.json` and its media folder.
