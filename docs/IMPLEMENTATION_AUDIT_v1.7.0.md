# Implementation Audit — v1.7.0

**Date:** 2026-04-06  
**Scope:** Repository-wide scan of tracked source and documentation for partial implementation markers (`TODO`, `NotImplemented`, `WIP`, `TBD`) and stale release labels.

## Summary

- Core .NET packages (`src/SharpCoreDB*`) are buildable and release-labeled on `1.7.0`.
- Main server and migration integration paths are buildable.
- Remaining partial implementation markers are concentrated in:
  - Python SDK (`src/SharpCoreDBServerScriptClients/python/pysharpcoredb`)
  - Benchmark/test scaffolding notes (non-production code paths)

## Active Partial-Implementation Areas

### 1) Python SDK (PySharpDB)

The Python client contains explicit placeholders and unimplemented transport calls, including:

- `connection.py`
  - `raise NotImplementedError("Query execution not implemented for current protocol")`
  - `raise NotImplementedError("Non-query execution not implemented for current protocol")`
  - `raise NotImplementedError("Ping not implemented for current protocol")`
- `grpc_client.py`
  - `TODO` markers for connect/disconnect/query/non-query/ping and streaming calls
- `http_client.py`
  - `TODO` marker for JWT authentication
- `types.py`
  - `TODO` marker for column-name lookup
- `ws_client.py`
  - `TODO` markers for auth header flow and server version retrieval

**Current status:** Python SDK is not at feature parity with the .NET client and should be treated as work-in-progress.

### 2) Test and benchmark backlog markers

`TODO` markers also exist in test and benchmark files. These are not production runtime blockers but indicate remaining validation/perf work.

## Documentation Actions Completed

- Normalized active release labels to `v1.7.0` / `1.7.0` where applicable.
- Added explicit current-state documentation for implementation completeness.
- Removed obsolete empty documentation artifacts where found.

## Release Readiness Interpretation

- **Core product status:** Release-ready for `v1.7.0` in .NET core/server packages.
- **SDK parity status:** Python SDK still requires implementation completion before claiming full protocol parity.

## Canonical Status References

- `README.md`
- `docs/PROJECT_STATUS.md`
- `docs/INDEX.md`
