# Status update workplace integration

Glance ships with compilable Microsoft Entra, Azure DevOps, and Microsoft Graph starter adapters. They are deliberately disabled by default. All workplace-specific code is isolated under `server/Integrations/`; search for `TODO(WORKPLACE_INTEGRATION)` before testing at work.

## Configuration

Set `StatusUpdates:Microsoft:Enabled` and the two source-specific `Enabled` settings in `server/appsettings.json` only after the workplace app registration is ready.

- `Microsoft.TenantId`: company Entra tenant ID
- `Microsoft.ClientId`: public/native app registration client ID; its redirect URI must allow the system browser flow
- Delegated permissions: company-approved Azure DevOps read access and Microsoft Graph `Mail.Read`. The starter requests Azure DevOps's `.default` scope, so the app registration controls its granted Azure DevOps permissions.
- `AzureDevOps.Organization`, `Project`, `SavedQueryId`: cloud Azure DevOps values. The query is read first, then its work item IDs are batch-read. Add optional field reference names in `AdditionalFields`.
- `Outlook.FolderId`: Graph mail-folder ID. `FolderDisplayName` is only a human-readable label. Message bodies are converted to bounded plain text; attachments are never read.

MSAL stores its protected token cache below `data/auth`. Authentication and both readers return a clear `notConfigured` status while disabled. The cache is not included in Glance backups.
The Status Updates view exposes a **Microsoft sign in / test** button; collecting input also acquires tokens when needed. Tokens are never returned to the UI.

## Data and retention

Each collection creates the ordinary `StatusSummary.json` download, but stores it internally as `data/status-updates/YYYY-MM-DD/StatusSummary.json.gz`. Recollecting on the same local calendar day reuses the report ID, increments `inputRevision`, and replaces that file. Earlier days remain. Backups include these already-compressed JSON files and omit generated Office files.

The input contains the prior completed output, every currently `📝`-marked Dashboard topic, Azure DevOps query results, and Outlook messages in the configured context window. By default, new evidence begins after the prior completed report (four weeks for the first report) and the wider context covers four weeks.

On import, Glance validates `schema/status-summary.schema.json`, the retained report ID and revision, the document status, and a SHA-256 hash of the unchanged input. Excel and PowerPoint are generated only from the validated typed output.

## ChatGPT handoff

Upload the downloaded JSON and schema to the dedicated project. The completed file must retain the input and integrity sections exactly, set `documentStatus` to `completed`, and fill `output.projectStatus`, `output.risks`, and `output.openQuestions`. Glance sets the final completion timestamp during import.
