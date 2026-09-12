Glance is a lightweight desktop app for capturing and organizing tasks with a fast, keyboard-friendly workflow. It combines a Photino-based desktop shell with a local .NET backend and a Vite-powered UI.

The app stores data locally and keeps the interface focused on quick capture, simple categorization, and minimal friction. It ships with a dashboard for active work, person-specific lists, project status packages, search, and history.

This repository contains the desktop host, server, and UI. Build and run from the solution file at the root, or use the UI and server projects directly during development.

To build a portable Windows x64 package, run:

```powershell
.\publish.ps1
```

The build machine needs Node.js/npm and the .NET 9 SDK. The complete application is produced in `portable/Glance/` (ignored by Git). Copy that entire folder to a USB stick, then to a writable folder on the production PC. Double-click `glance.exe` to run. No environment variables, launcher scripts, Node.js, or separate .NET installation are needed on the production PC; Windows needs WebView2.

Release builds always use the bundled UI and store data beside the executable, regardless of the working directory or development environment variables. The package includes the UI, schema, migrations, and runtime dependencies, but no development data. Rebuilding replaces the old package; the script refuses to replace a package containing runtime data. Build staging files are retained under the ignored `artifacts/` folder.

For development, start Vite in one PowerShell terminal:

```powershell
cd ui
npm ci
npm run dev
```

In another terminal at the repository root:

```powershell
dotnet run --project desktop/Glance.Desktop.csproj -c Debug
```

Debug builds always use Vite (default `http://localhost:5173/`). `GLANCE_DEV_SERVER_URL` can optionally customize that URL for Debug builds only. `GLANCE_USE_DEV_SERVER` is no longer used. Production builds also ignore `GLANCE_APP_ROOT`; Debug builds still support it for isolated development data.

Workplace setup for the disabled-by-default Azure DevOps and Outlook status readers is documented in [docs/status-updates-integration.md](docs/status-updates-integration.md).

Backup verification, restore behavior, retention, and recovery mode are documented in [docs/data-safety.md](docs/data-safety.md). The neutral exit bundle is documented in [docs/data-portability.md](docs/data-portability.md).

Useful editor shortcuts include Ctrl+B/Ctrl+I for formatting, Ctrl+K for web or file links, Ctrl+F for Search, and Ctrl+Z/Ctrl+Y for session undo/redo. In an editable note, Ctrl+click opens a hyperlink; links in read-only Search and History views open with a normal click.

