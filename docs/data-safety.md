# Data safety and recovery

Glance treats the database, note images, and retained status JSON as one recoverable state. Generated Excel and PowerPoint files are deliberately excluded because they can be generated again.

## Automatic safeguards

- Before migrations, Glance opens the existing database read-only and checks SQLite structure, foreign keys, supported schema version, rich-note JSON, and referenced files. Migrations do not run if this check fails.
- A verified pre-migration backup is required before an upgrade can change the database. For a manual app upgrade, close all windows and copy the application files while preserving the existing data folders; the database is migrated on the next launch.
- A failed startup check enters recovery mode. Glance leaves the database, WAL, and SHM files untouched and blocks note-changing APIs.
- Glance never automatically runs `VACUUM` as a repair or swaps in a rebuilt database. In SQLite, `VACUUM` means rebuilding the database into a compact copy; it is useful maintenance in some applications, but an automatic rebuild could hide damage or discard evidence needed for recovery.
- Every connection enables SQLite foreign-key enforcement.
- Ordinary note deletion is a soft deletion for 30 days. Dashboard and People session undo restore the same row ID.
- Orphaned images move to `blobs/attachment-trash` for 30 days. Title and subcontent images are both scanned, and a parsing failure retains files rather than deleting them.
- Permanent cleanup only runs after a verified backup exists that is new enough to contain the recoverable state.

## Verified restore points

When notes have changed and Glance is running, the default schedule retains up to 48 hourly, 30 daily, and 12 monthly restore points. Unchanged hours do not create duplicate snapshots. Status packages remain compressed inside backup ZIPs, and only the latest same-day status package is retained before backup.

Each version-2 backup is built as a partial file and promoted only after verification. `backup-manifest.json` records its reason, application and schema versions, timestamps, content counts, last change ID, and SHA-256 hash/size for every file. Verification rejects unsafe/duplicate ZIP paths, missing or changed files, an unhealthy archived SQLite database, foreign-key failures, invalid rich JSON, and missing referenced images/status files. Older legacy ZIPs receive the strongest layout and database checks possible, but cannot gain an original manifest retroactively.

When both local and second-location copies exist, verification and restore try local copies first and then any matching second-location copies. A damaged local ZIP therefore does not hide a valid mirror. If every copy fails a check, the restore-point catalog marks that backup as failed for the current session until a later verification succeeds.

Settings can specify an optional second location on a mapped drive, UNC share, USB drive, or another folder. Glance always attempts the local verified backup first. If the second location is offline or unwritable, the local copy remains successful and the UI shows a retryable warning. Use the button labelled **Test location availability and write permissions.** before relying on a location.

The newest backup is periodically reverified without touching live data.

## Restoring

Settings lists actual available snapshots with exact times, verification state, counts, reason, and copy locations. There is no arbitrary timestamp field: select one of the snapshots that really exists.

While Glance coordinates a manual backup, export, update, restore, or application close, every open window is briefly made read-only until its pending saves finish. A failed save cancels the operation and leaves the edited window open.

Restore performs these steps:

1. flush pending saves in every open Glance window;
2. verify and stage the selected backup by its internal backup ID;
3. create and verify an emergency backup of the current state;
4. restart Glance;
5. before opening or migrating SQLite, move the current state into a rescue folder and install the staged state;
6. verify the installed database; and
7. roll back from the rescue folder if installation verification fails.

The rescue state remains under the app's `recovery/` folder for manual investigation. Upload filenames are never accepted as restore paths.

If a staged restore is invalid or installation fails, its plan, staging data, and diagnostics are moved under a `recovery/failed-restore-*` folder and the active pending instruction is cleared. After a successful rollback, Glance will not silently retry the failed plan; Settings can stage another restore point. If an immediate restart is cancelled because a note could not save, Settings continues to show **Restore pending** and the close warning explains that the staged restore will apply on a later restart.

## Tests versus live maintenance

Normal runtime checks are read-only verification and snapshot creation. Destructive restore drills and injected failure cases run only against temporary test app roots during `dotnet test`; Glance never schedules a destructive drill against live notes. The test suite covers corrupted/hashes/missing files, path traversal, startup-before-migration checks, migration backup requirements, soft-deletion recovery, attachment quarantine, and restore rollback behavior.

For disaster recovery if the entire computer or app folder is lost, configure the optional second location and occasionally confirm that it contains recent verified ZIPs. No local-only design can survive loss of the disk holding both live data and local backups.
