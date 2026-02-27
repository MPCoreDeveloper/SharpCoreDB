# Sync Provider Integration Tests

This folder contains end-to-end and integration validation for `SharpCoreDB.Provider.Sync`.

## Test Suites

- `SyncIntegrationTestBase.cs`
  - Shared setup/teardown
  - Creates isolated SharpCoreDB and SQLite databases per test
  - Provides helper methods for schema setup, data insertion, and sync execution

- `SQLiteRoundtripTests.cs`
  - Bidirectional transfer validation
  - CRUD propagation
  - Multi-table behavior

- `EndToEndSyncScenarios.cs`
  - Real-world flows (offline-first, incremental sync, migration/restore scenarios)

- `SyncPerformanceBenchmarks.cs`
  - Throughput and scalability checks
  - Large data set behavior
  - Batch behavior validation

- `SyncErrorHandlingTests.cs`
  - Invalid schema/table paths
  - Recovery/error-path behavior
  - Robustness checks

## Current Status

- Integration tests are **enabled**
- Included in provider test suite (`84/84` total passing)

## Run

```bash
dotnet test tests/SharpCoreDB.Provider.Sync.Tests/SharpCoreDB.Provider.Sync.Tests.csproj --filter "Integration"
