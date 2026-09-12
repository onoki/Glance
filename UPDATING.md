# Updating Glance

## Update steps
1. Prefer **Settings → App updates → Install update**.
2. Glance flushes open-window edits and requires a verified pre-update backup before applying the package.
3. Update packages cannot contain or replace `data/`, `blobs/`, `backups/`, `recovery/`, or `exports/`.
4. If replacing binaries manually, close every Glance window first and leave those protected folders untouched.

## Restore from backup
Use **Settings → Data safety → Restore an earlier snapshot**. Choose one of the actual verified restore points shown by Glance. It creates an emergency backup, restarts, installs the staged database/images/status JSON before startup writes, verifies the result, and preserves the previous state in `recovery/`.

Manual file copying is an emergency-only procedure. Keep the original database, WAL, SHM, attachments, and status files together and untouched; consult [docs/data-safety.md](docs/data-safety.md) before changing them.
