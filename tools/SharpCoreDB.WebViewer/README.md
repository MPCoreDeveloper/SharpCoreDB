# SharpCoreDB WebViewer — Primary Studio Tool

> **📦 Moving to its own repository:** this tool is becoming **SCDMS — Sharp Core Database Management System**, hosted at [`github.com/MPCoreDeveloper/SCDMS`](https://github.com/MPCoreDeveloper/SCDMS). New development happens there. SCDMS migrates your existing `%LOCALAPPDATA%\SharpCoreDB.WebViewer` data automatically on first start. See [`docs/viewer/scdms-standalone-plan.md`](../../docs/viewer/scdms-standalone-plan.md) for the full plan.

**This is the recommended database studio for SharpCoreDB.** It replaces the legacy `SharpCoreDB.Viewer` Avalonia desktop application, which is now **deprecated**.

SharpCoreDB WebViewer is a local-first Razor Pages application for inspecting and operating SharpCoreDB databases with secure defaults.

## Key capabilities

- Local connection mode (directory or single-file)
- Network server connection mode (SharpCoreDB gRPC server)
- SafeWebCore strict A+ security profile
- SQL editor with named parameter JSON payloads
- Result grid for executed SELECT statements
- Table explorer and metadata browser (columns, indexes, triggers)
- Transaction controls (begin, commit, rollback)
- Saved query library with scoped visibility per connection target
- Query execution history with success/failure status
- Workspace import/export as JSON

## Built-in databases

The viewer ships with one default database and two sample databases:

| Database | Purpose | Created when |
|---|---|---|
| `scdb` | Default scratch database with a small `welcome` table | Automatically on first launch |
| `contoso` | Retail sample (customers, products, orders, order items, inventory) | On demand (sidebar **Database Actions** or **File** menu) |
| `adventureworks` | Cycles manufacturer sample (products, customers, sales orders, territories) | On demand (sidebar **Database Actions** or **File** menu) |

**Password:** all built-in databases are created with the default password **`scdb`**.
You need it when reconnecting manually (recent connection profiles intentionally do not persist passwords).

**Storage location:** `%LOCALAPPDATA%\SharpCoreDB.WebViewer\Data\<name>` (e.g. `C:\Users\<you>\AppData\Local\SharpCoreDB.WebViewer\Data\contoso`).

**Changing the defaults:** edit `appsettings.json`:

```json
"WebViewer": {
  "DefaultDatabaseName": "scdb",
  "DefaultDatabasePassword": "scdb",
  "DefaultDatabasePath": "",
  "SampleDatabasesDirectory": ""
}
```

- `DefaultDatabasePassword` applies to newly created built-in databases. Existing databases keep the password they were created with — delete the database folder to re-create it with a new password.
- `DefaultDatabasePath` overrides the storage folder of the default database.
- `SampleDatabasesDirectory` overrides the root folder for all built-in databases.

## Security posture

The WebViewer is configured secure-by-default:

- HTTPS-only endpoint binding
- Session cookie with HttpOnly, Secure, and SameSite=Lax
- SafeWebCore strict A+ headers enabled
- CSP nonce support in layout
- No password persistence in recent connection profiles

## Connection modes

### Local mode

Use this when the viewer runs directly against local SharpCoreDB storage:

- `LocalDatabasePath`
- `LocalStorageMode` (Directory or SingleFile)
- `LocalReadOnly`
- `Password`

### Server mode

Use this when connecting to SharpCoreDB server:

- `ServerHost`
- `ServerPort`
- `ServerDatabase`
- `ServerUsername`
- `ServerUseSsl`
- `ServerPreferHttp3`
- `Password`

## SQL editor and parameters

The SQL editor supports multiple statements in one execution.

Use the **Parameters (JSON object)** field for named parameters:

```json
{
  "@id": 10,
  "@name": "Alice"
}
```

Supported JSON-to-parameter conversions include:

- `null`
- `bool`
- numeric values (`int`, `long`, `decimal`, `double`)
- `string`
- arrays

## Transactions

Transaction controls are available in the SQL panel:

- **Begin** starts a transaction for the current session
- **Commit** commits active transaction
- **Rollback** rolls back active transaction

When a transaction is active, query execution reuses the same transaction-scoped connection.

## Saved queries and history scopes

Saved queries and history are scoped by connection target:

- Global items (no target key) are always visible
- Target-specific items are visible only when connected to that target

Scope examples:

- local: `local:c:\data\mydb`
- server: `server:localhost:5001/master`

## Workspace import/export

The viewer can export/import query workspace state as JSON:

- Saved queries
- Query history

Use the **Workspace Import/Export** panel:

1. Export to JSON
2. Copy payload for backup
3. Paste payload and import when restoring

## Persistence paths

The viewer stores local user data under:

`%LOCALAPPDATA%\SharpCoreDB.WebViewer\`

Files:

- `settings.json` (recent connections)
- `query-workspace.json` (saved queries and history)

## Build and run

From repository root:

```powershell
dotnet build tools/SharpCoreDB.WebViewer/SharpCoreDB.WebViewer.csproj
dotnet run --project tools/SharpCoreDB.WebViewer/SharpCoreDB.WebViewer.csproj
```

## Operational notes

- Keep TLS enabled for server mode in production.
- Use strong database/server passwords.
- Prefer scoped saved queries per target to avoid accidental cross-environment execution.
- Clear history periodically if it may contain sensitive statement previews.


