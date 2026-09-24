# Copilot Instructions

## General Guidelines
- Test programs should be located in the `tests` folder and not in the repository root.
- All project documentation must be written in English. This includes docs, README files, technical specs, implementation plans, and code comments.
- Provide periodic progress updates while work is ongoing and do not remain stuck on the same point for long stretches.
- Before considering work ready for commit in this repository, always verify code coverage and confirm the coverage threshold passes.
- When benchmarking competitor databases (like BLite), document the developer experience (DX) honestly. If a library's API is hard to use, poorly documented, or has mismatches between docs and actual API, note that as a real finding in the benchmark report. User-friendliness and ease of integration matter as much as raw performance numbers.
- Standardize all documentation/version labels to the latest version.
- Continue implementation until the scoped roadmap work is finished without pausing for confirmation.
- When roadmap issues are completed, explicitly mark the corresponding issue draft documents as resolved/completed so the status is visibly updated in the repository.
- Validate issue status claims against current open issues and actively work through unresolved issues, ensuring they are not considered done prematurely.

## Testing Policy
- All test projects in SharpCoreDB must use **xUnit v3** (`xunit.v3` NuGet package, currently 4.0.0). **Never** use `xunit` v2 (package id `xunit`). The old v2 package is incompatible with .NET 11 / C# 15.
- Use `xunit.runner.visualstudio` 4.0.0+ for test discovery.
- If you encounter any project referencing `xunit` (without `.v3`), migrate it to `xunit.v3` immediately.
- Test runner: `Microsoft.NET.Test.Sdk` 18.9.0+ (latest stable for .NET 11).
- `dotnet test` no longer works here: Microsoft.Testing.Platform (xunit.v3) rejects the VSTest target on .NET 10 SDK and later. Run the MTP test host directly, e.g. `tests/SharpCoreDB.Tests/bin/Release/net11.0/SharpCoreDB.Tests.exe -filterVSTest "FullyQualifiedName~MyTestClass"`.
- Prefer targeted, fast test runs instead of broad/long-running full-suite test execution during iterative work. Query failed tests directly instead of performing broad test scans, especially when a single hanging test is the bottleneck.

## Code Style
- Formatting follows `.editorconfig` (4-space C# indent, UTF-8, final newline). Change whitespace with `dotnet format`, never by hand.
- Use only modern C# 15 preview code patterns in this repository (`LangVersion=preview`, `net11.0` on the v2.1 line).
- Use native .NET 11 code and C# 15 across SharpCoreDB; do not suggest downgrading framework or assuming pre-.NET 11 context.

## Package Policy
- All package versions are declared centrally in `Directory.Packages.props` (central package management is on). Never add a `Version` attribute to an individual `.csproj`; CI fails the build on deprecated packages.
- Prefer Microsoft-backed packages by default.
- If a non-Microsoft package is used (e.g., Serilog), keep it on latest stable and avoid deprecated versions.
- Avoid prerelease packages unless explicitly requested — the deliberately pinned .NET 11 RC SDK in `global.json` is a standing exception, not a precedent for dependencies.
- Favor modular package design with production dependencies flowing through transitive NuGet references to core packages.

## Project-Specific Rules
- Require full SQLite compatibility: SharpCoreDB sync and provider must support all SQLite syntax/features users could use, never less; extra capabilities are fine.
- SharpCoreDB Server must support multiple databases and system databases, and must enforce HTTPS/TLS (minimum TLS 1.2) with no plain HTTP endpoints.
- New SharpCoreDB features must remain optional; event sourcing must be delivered as a separate NuGet package, and issue-driven user features should be prioritized ahead of server mode work.
- Event sourcing must support both persistent storage and the existing in-memory option; provide an additional demo example specifically for persistent storage.
- Prioritize gRPC as the flagship protocol for SharpCoreDB.Server; binary/HTTP are secondary.
- Keep optional packages (ES/CQRS and the rest) .NET 11-native, and keep zero external dependencies in the core package.
- The graphical UI (formerly SharpCoreDB.WebViewer / SharpCoreDB.Viewer), including the table designer's type dropdown (ULID and GUID included), lives in the standalone repo MPCoreDeveloper/SCDMS. Keep it secure-by-default and, when integrating SafeWebCore, use the strict A+ settings/profile. Support both local database connections and network SharpCoreDB server connections, aiming to expose broad feature coverage from available SharpCoreDB NuGet capabilities.
- Distinguish native SharpCoreDB syntax from PostgreSQL syntax and SQLite syntax; only apply SQLite-specific engine restrictions when SQLite syntax is explicitly relevant.
- Use the full SqlParser for DDL in SingleFileDatabase to support advanced features like those in FluentMigrator.

## Asynchronous Programming Guidelines
- When fixing cancellation token handling in parallel async methods that use `Parallel.ForEachAsync`, always wrap the parallel operation in a try-catch block to properly propagate `OperationCanceledException`. The `CancellationToken` passed to `ParallelOptions` will cause an `OperationCanceledException` to be thrown from `Parallel.ForEachAsync`, and this must be caught and re-thrown to ensure proper cancellation propagation to calling code.
