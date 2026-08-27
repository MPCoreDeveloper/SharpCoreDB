# SCDMS — The UI has moved to a standalone repository

> **Status:** UI migration complete · **Date:** 2026-08-27

The graphical user interfaces of SharpCoreDB are **no longer part of this repository**.
They have been migrated to their own standalone repository:

## 🔗 [github.com/MPCoreDeveloper/SCDMS](https://github.com/MPCoreDeveloper/SCDMS)

**SCDMS** (Sharp Core Database Management System, by analogy to SSMS) contains the full UI layer:

| Formerly (this repo) | Now (SCDMS) |
|---|---|
| `tools/SharpCoreDB.Viewer` — Avalonia desktop viewer | SCDMS desktop / web management environment |
| `tools/SharpCoreDB.WebViewer` — Razor Pages web admin portal | SCDMS (web installation, self-contained binaries, update mechanism) |
| `tests/SharpCoreDB.Viewer.Tests` | moved to SCDMS |
| `docs/viewer/*` — viewer documentation | moved/rewritten in SCDMS |

This repository (`MPCoreDeveloper/SharpCoreDB`) now only contains the **database engine and the
programmatic access layers**: `SharpCoreDB` core, ADO.NET / EF Core provider, `SharpCoreDB.Client`
(gRPC), `SharpCoreDB.Server`, function libraries and extension packages. There is no UI code or UI
project left in this solution (`SharpCoreDB.sln` / `SharpCoreDB.slnx`).

## Why

- The UI had its own release cadence, distribution model (self-contained single-file binaries) and
  install/update mechanism that is decoupled from the engine.
- SCDMS can release and build independently against `SharpCoreDB` (and the provider/client) through
  NuGet.org, so engine changes no longer block UI releases (or vice versa).

## For developers

- **Engine/API work** (this repo): no UI components anymore; build with `dotnet build SharpCoreDB.slnx`.
- **UI work**: go to [MPCoreDeveloper/SCDMS](https://github.com/MPCoreDeveloper/SCDMS).
- Historical UI plans and documentation (such as `web-viewer-razor-pages-plan.md` and
  `scdms-standalone-plan.md`) are not preserved in this repository; the current plan and
  documentation live in the SCDMS repository.

