# Copilot Instructions

## General Guidelines
- Test programs should be located in the `tests` folder and not in the repository root.
- All project documentation must be written in English. This includes docs, README files, technical specs, implementation plans, and code comments.
- Provide periodic progress updates while work is ongoing to ensure the assistant is not stuck.
- When benchmarking competitor databases (like BLite), document the developer experience (DX) honestly. If a library's API is hard to use, poorly documented, or has mismatches between docs and actual API, note that as a real finding in the benchmark report. User-friendliness and ease of integration matter as much as raw performance numbers.

## Code Style
- Use specific formatting rules.
- Follow naming conventions.
- Use native .NET 10 code and C# 14 across SharpCoreDB; do not suggest downgrading framework or assuming pre-.NET 10 context.

## Package Policy
- Prefer latest stable released NuGet packages and package versions.
- Prefer Microsoft-backed packages by default.
- If a non-Microsoft package is used (e.g., Serilog), keep it on latest stable and avoid deprecated versions.
- Avoid prerelease packages unless explicitly requested.

## Project-Specific Rules
- Custom requirement A.
- Custom requirement B.
- Require full SQLite compatibility: SharpCoreDB sync and provider must support all SQLite syntax/features users could use, never less; extra capabilities are fine.
- SharpCoreDB Server must support multiple databases and system databases, and must enforce HTTPS/TLS (minimum TLS 1.2) with no plain HTTP endpoints.
- New SharpCoreDB features must remain optional; event sourcing must be delivered as a separate NuGet package, and issue-driven user features should be prioritized ahead of server mode work.
- Event sourcing must support both persistent storage and the existing in-memory option; provide an additional demo example specifically for persistent storage.
