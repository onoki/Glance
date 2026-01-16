# Iteration 1 - Build and Run

## Build the UI (production)

1. `cd ui`
2. `npm install`
3. `npm run build`

The output is written to `ui/dist` and served by the backend.

## Run the desktop app (production UI)

1. `dotnet build Glance.sln`
2. `dotnet run --project desktop/Glance.Desktop.csproj`

## Run with the Vite dev server

1. In one terminal:
   - `cd ui`
   - `npm install`
   - `npm run dev`
2. In another terminal:
   - PowerShell: `$env:GLANCE_DEV_SERVER_URL = "http://127.0.0.1:5173/"`
   - Cmd: `set GLANCE_DEV_SERVER_URL=http://127.0.0.1:5173/`
   - `dotnet run --project desktop/Glance.Desktop.csproj`

## Iteration 1 scope

Implemented:
- Photino desktop host that starts the local ASP.NET Core backend.
- SQLite initialization with schema + migrations in `docs/migrations`.
- Dashboard tab with editable New and Main task lists.
- Task CRUD, completion, persistence, and change polling.
- Basic multi-instance consistency via `changes` polling.

Deferred:
- Recurrence logic and derived category rules beyond the two lists.
- History and search UI features.
- Rich editor invariants and Tiptap editor integration.
- Search highlighting and charting.
