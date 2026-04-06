# Copilot Instructions

## General Guidelines
- Test programs should be located in the `tests` folder and not in the repository root.
- All project documentation must be written in English. This includes docs, README files, technical specs, implementation plans, and code comments.
- Provide periodic progress updates while work is ongoing to ensure the assistant is not stuck.
- When benchmarking competitor databases (like BLite), document the developer experience (DX) honestly. If a library's API is hard to use, poorly documented, or has mismatches between docs and actual API, note that as a real finding in the benchmark report. User-friendliness and ease of integration matter as much as raw performance numbers.
- Standardize all documentation/version labels to v1.6.0 ("V 1.60").
- Continue implementation until the scoped roadmap work is finished without pausing for confirmation.
- When roadmap issues are completed, explicitly mark the corresponding issue draft documents as resolved/completed so the status is visibly updated in the repository.

## Testing Policy
- All test projects in SharpCoreDB must use **xUnit v3** (`xunit.v3` NuGet package, currently 3.2.2+). **Never** use `xunit` v2 (package id `xunit`). The old v2 package is incompatible with .NET 10 / C# 14.
- Use `xunit.runner.visualstudio` 3.1.5+ for test discovery.
- If you encounter any project referencing `xunit` (without `.v3`), migrate it to `xunit.v3` immediately.
- Test runner: `Microsoft.NET.Test.Sdk` 18.3.0+ (latest stable for .NET 10).
- Prefer targeted, fast test runs instead of broad/long-running full-suite test execution during iterative work.

## Code Style
- Use specific formatting rules.
- Follow naming conventions.
- Use only modern C# 14 code patterns in this repository.
- Use native .NET 10 code and C# 14 across SharpCoreDB; do not suggest downgrading framework or assuming pre-.NET 10 context.

## Package Policy
- Prefer latest stable released NuGet packages and package versions.
- Prefer Microsoft-backed packages by default.
- If a non-Microsoft package is used (e.g., Serilog), keep it on latest stable and avoid deprecated versions.
- Avoid prerelease packages unless explicitly requested.
- Favor modular package design with production dependencies flowing through transitive NuGet references to core packages.

## Project-Specific Rules
- Custom requirement A.
- Custom requirement B.
- Require full SQLite compatibility: SharpCoreDB sync and provider must support all SQLite syntax/features users could use, never less; extra capabilities are fine.
- SharpCoreDB Server must support multiple databases and system databases, and must enforce HTTPS/TLS (minimum TLS 1.2) with no plain HTTP endpoints.
- New SharpCoreDB features must remain optional; event sourcing must be delivered as a separate NuGet package, and issue-driven user features should be prioritized ahead of server mode work.
- Event sourcing must support both persistent storage and the existing in-memory option; provide an additional demo example specifically for persistent storage.
- Prioritize gRPC as the flagship protocol for SharpCoreDB.Server; binary/HTTP are secondary.
- SharpCoreDB roadmap priority: make Event Sourcing first-class, add native snapshots, then projection engine; keep ES/CQRS optional packages, .NET 10-native, zero external deps in core, and gRPC-first networked server integration.

## Asynchronous Programming Guidelines
- When fixing cancellation token handling in parallel async methods that use `Parallel.ForEachAsync`, always wrap the parallel operation in a try-catch block to properly propagate `OperationCanceledException`. The `CancellationToken` passed to `ParallelOptions` will cause an `OperationCanceledException` to be thrown from `Parallel.ForEachAsync`, and this must be caught and re-thrown to ensure proper cancellation propagation to calling code.
