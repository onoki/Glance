# Windows integration

Glance uses the stable process AppUserModelID `Glance.Desktop`, set before creating any window. All windows belong to that identity. The portable app creates/refreshes `Glance.lnk` beside `glance.exe` on startup, with the same ID, a capitalized description, and the executable icon. The executable filename stays lowercase for existing deployment scripts; Windows-facing product and file-description metadata say Glance.

After copying an updated build to its final folder, run `glance.exe` once. If Windows keeps showing a separate pinned launcher, unpin the old entry and pin the generated **Glance** shortcut (Windows 11 may put Pin to taskbar under Show more options). Existing pins can retain older targets, identities, and cached names. Keep the portable folder at a stable path. Glance does not rearrange taskbar buttons or alter Windows taskbar/monitor settings. If the app folder is read-only, shortcut creation is logged as unavailable and the app still runs.

A single window is titled **Glance**. With multiple windows, captions are **G - Main** and **G - Secondary**. The helper's lightly blue navigation bar and plain Helper window label remain. Closing a helper saves and closes that helper. Closing Main with helpers open first asks whether to close all windows (No is the default); accepting uses the existing coordinated save/close flow, and a failed save cancels closing. Restore-triggered restart uses its existing confirmation and save flow.

Tests: `WindowPresentationTests` covers caption/count policy and actual Windows shortcut property-store round-tripping; `desktopLifecycle.test.js` covers save-before-close behavior. Live taskbar placement still requires checking the destination computer's existing pin and Windows taskbar configuration.

Reference: [Microsoft AppUserModelID guidance](https://learn.microsoft.com/en-us/windows/win32/shell/appids) describes matching process and shortcut identities for taskbar grouping.
