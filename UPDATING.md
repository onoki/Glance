# Updating Glance

## Update steps
1. Close the app.
2. Replace the app binaries with the new version.
3. Keep the `data/` and `blobs/` folders untouched.

## Restore from backup
1. Close the app.
2. Copy the backup `glance.db` (and any `-wal` or `-shm` files) into `data/`.
3. Copy attachment files into `blobs/attachments/` if needed.
4. Start the app.
