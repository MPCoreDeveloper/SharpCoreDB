# SharpCoreDB Viewer v2 – Usage & Limitations

**Version:** v1.7.0  
**Component:** `tools/SharpCoreDB.Viewer`  
**Last updated:** 2025

---

## Overview

SharpCoreDB Viewer is a first-party cross-platform desktop utility for exploring, querying, and managing SharpCoreDB databases. It is built with **Avalonia UI** and targets **.NET 10**.

Starting with v1.7.0 (Viewer v2 foundation), the tool delivers:

- Connection profile management for quick reconnects.
- Richer database/table browser with live filter and metadata inspection.
- Improved query/result grid workflow with multi-statement support.
- Keyboard shortcuts for power-user efficiency.

---

## Getting Started

### Launching the Viewer

Run directly from the solution root:

```bash
dotnet run --project tools/SharpCoreDB.Viewer
```

Or publish a self-contained binary:

```bash
dotnet publish tools/SharpCoreDB.Viewer -c Release -r win-x64 --self-contained true
```

### Connecting to a Database

1. Click **Connect** on the toolbar or the welcome screen button.
2. The **Connect to SharpCoreDB** dialog opens.

#### Directory-based database

Enter or browse to a folder that contains your SharpCoreDB database files. If the folder does not exist it will be created automatically.

#### Single-file database (.scdb)

Enter or browse to a `.scdb` file. A new file will be created if it does not exist.

#### Recent Connections

Previously used databases appear in the **Recent Connections** dropdown at the top of the dialog.

- **Use** – Fills the path and storage-mode fields from the selected profile.
- **Remove** – Deletes the profile from the saved list.

Up to **8** recent profiles are persisted in `%LOCALAPPDATA%\SharpCoreDB.Viewer\settings.json`.

---

## Database Explorer (Left Panel)

| Feature | Description |
|---------|-------------|
| **Table list** | All user tables in the connected database listed alphabetically. |
| **Filter box** | Type to live-filter table names (case-insensitive, partial match). |
| **Refresh** | Reloads the table list from the current database schema. |
| **Preview Table** | Injects `SELECT * FROM "<table>" LIMIT 200;` into the editor and executes it immediately. |
| **Columns pane** | Column names, types, and NULL-ability from `PRAGMA table_info`. |
| **Indexes pane** | Index names from `sqlite_master`. |
| **Triggers pane** | Trigger names from `sqlite_master`. |

Selecting a table in the list automatically populates all metadata panes and enables the **Preview Table** button.

---

## SQL Query Editor (Right Top Panel)

The monospace query editor supports:

- **Multi-statement execution** – Separate statements with `;`. All statements run in order. The last `SELECT` result is shown in the grid.
- **Syntax-free scrolling** – Horizontal scroll is enabled for wide queries.

### Keyboard shortcuts

| Shortcut | Action |
|----------|--------|
| **F5** | Execute all statements in the editor |
| **Ctrl+Enter** | Execute all statements in the editor |

---

## Result Grid (Right Bottom Panel)

- Columns are generated dynamically for each query.
- Supports reorder, resize, and sort by column header.
- Alternating row colours for readability.
- Null and `DBNull` values appear as empty cells.
- Column ellipsis truncates long text; resize the column to see full values.

---

## Settings

Access via the **Settings** menu:

| Setting | Options |
|---------|---------|
| **Language** | English, Dutch, German, French, Spanish, Italian |
| **Theme** | Light, Dark |

Changes are applied live. Click **Save Settings** to persist across restarts.

---

## Tools Dialog

Access via the **Tools** menu. Provides database format conversion:

- Directory ↔ Single-file (.scdb)
- Uses the `SharpCoreDB.Data.Provider` conversion pipeline.

---

## Limitations (v1.7.0)

| Area | Limitation |
|------|-----------|
| **Editing** | The result grid is read-only. Use the SQL editor for modifications. |
| **Row limit** | Preview Table is limited to 200 rows. Write a custom query for full scans. |
| **Metadata** | Column statistics and constraint details require manual `PRAGMA` queries. |
| **Multi-database** | Only one database connection is active at a time. |
| **Authentication** | Password is entered per session; it is not stored in profiles for security. |
| **SQL highlighting** | The editor does not perform syntax highlighting in v1.7.0. |
| **Export** | No export to CSV/JSON in this release; copy cells manually. |

---

## Profile Storage

Connection profiles (path + storage mode + last-used timestamp) are saved to:

```
%LOCALAPPDATA%\SharpCoreDB.Viewer\settings.json
```

Passwords are **never** stored. You must re-enter the password on each connection.

---

## Troubleshooting

### "Not connected" after selecting a recent profile

The **Use** button pre-fills the path and format fields only. You still need to enter the password and click **Connect**.

### Tables do not appear after connecting

Click **Refresh** in the explorer panel. This can happen after schema changes from an external tool.

### Metadata pane shows "None available"

The selected table has no indexes or triggers defined. This is expected for simple tables.
