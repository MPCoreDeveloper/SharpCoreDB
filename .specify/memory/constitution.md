# SharpCoreDB Constitution

## Core Principles

### I. Security and Durability Are Non-Negotiable

Every database MUST be encrypted at rest by default with AES-256-GCM. Encryption MUST NOT be
disabled, weakened, or bypassed to win a benchmark, a test, or a demo. `NoEncryptMode` is a
measurement and diagnostics opt-out only and MUST NOT become a default or a shipped posture.

Durability guarantees MUST hold on every supported storage shape: WAL semantics, crash consistency,
the reopen round-trip matrix, and single-file/directory parity. A change that trades confidentiality
or durability for speed is rejected regardless of the measured gain.

Rationale: the product promise is SQLite-like simplicity with enterprise-grade security. An engine
that is faster because it wrote plaintext, dropped a commit, or lost data on reopen has not improved
the product — it has broken it.

### II. SQLite Compatibility Is a Floor, Not a Ceiling

Supported SQLite syntax and features MUST never be less than SQLite's own; additional capability is
allowed and encouraged. A new or changed SQL surface MUST carry a compatibility test that fails if
the SQLite-compatible behaviour regresses.

The provider family — ADO.NET, EF Core, Dapper, linq2DB, YesSql and Sync — MUST remain conformant to
the contracts of the libraries it plugs into. A provider shortcut that changes observable ADO.NET or
ORM behaviour is a defect, not an optimisation.

Rationale: migration from SQLite and drop-in provider support are the adoption path. Any gap that
forces users to rewrite working SQL breaks that path, however fast the engine is.

### III. Measured Performance, Reported Honestly

- Every performance claim MUST be backed by same-run measurements from this repository's harness and
  cited to the file the number comes from. External sources may support mechanisms only; they never
  supply a SharpCoreDB number.
- Comparisons against SQLite MUST be stated as ratios, never absolutes. SQLite's own reference drifts
  between sessions and runs.
- Encrypted (default) and `NoEncryptMode=true` results MUST be reported side by side, never one alone.
- Only the `--pk` arm is like-for-like against SQLite's `INTEGER PRIMARY KEY`. Any other arm MUST be
  labelled as such wherever it is cited.
- A comparison is invalid when the reference is handicapped — for example, allocating a command and
  parameters per row. Such a reading MUST be withdrawn, not explained away.
- A verdict MUST be read from the code that produces the number. A ratio alone is never sufficient
  evidence, and an inferred cause is never a licence to change code.
- Unflattering results, regressions and withdrawn readings MUST stay in the record.
- The fixed-width layout default MUST NOT be flipped: the measured effect on the PK-less shape is a
  24 % regression, and changing the default requires an explicit owner decision with new evidence.

Rationale: benchmarks are this product's evidence, and an unverified ratio has already cost this
project real work. Honest, reproducible numbers are worth more than a favourable headline.

### IV. Verified Quality: Red-to-Green Fixes and Green Canaries

- A defect found MUST be fixed in the session that finds it. The minimum evidence is a test that
  fails before the fix and passes after it, a clean build, a green core suite, and the result
  recorded — the worklog for performance work, `docs/CHANGELOG.md` when behaviour is user-visible.
- Fix the cause, not the symptom. The code that produces the wrong value MUST be read before a fix
  is chosen, and where the same logic is duplicated every copy MUST be corrected.
- The wider core suite MUST be run after a fix, not only the new tests.
- These regression canaries MUST stay green: `ReopenRoundTripMatrixTests`, `FormatCompatPolicyTests`,
  `FixedWidthBulkUpdateTests`, `FixedWidthBulkDeleteTests`, `FixedWidthPatchTests`,
  `WritePathProfilerTests`, `FixedWidthInlineValueTests.Reopen_KeepsInlineAndOverflowValues` and
  `SingleFileDirectoryParityTests`.
- New tests MUST be named `MethodName_StateUnderTest_ExpectedBehavior`, and test projects MUST use
  `xunit.v3`.
- A failing test MUST NOT be edited until it passes, and no guarantee may be weakened to obtain green.
- CI MUST keep excluding `Debug`, `Manual` and `Performance` categories by default; new slow or
  benchmark tests MUST carry `[Trait("Category","Performance")]`.

Rationale: the full suite has twice caught a regression the focused tests missed, and the two
defects of session 62 were found only because a fix was demanded in the same session. Quality is
demonstrated by failing-first evidence, not by assertion.

### V. Optional, Modular Capability

- New features MUST ship optional and off by default unless the owner amends this constitution.
- Event Sourcing MUST ship as its own NuGet package, and every package in the family MUST stay
  independently consumable.
- Issue-driven user-facing features outrank server-mode work.
- gRPC is the flagship server protocol; the binary and HTTP transports are secondary.
- The server MUST support multiple user databases plus system databases.
- The graphical UI lives in `MPCoreDeveloper/SCDMS` and MUST NOT be re-added to this repository.
- Feature work MUST NOT change package versions, `global.json` or release metadata; NuGet versions
  live in `Directory.Packages.props` under central package management.
- Public API changes MUST follow semantic versioning, and breaking changes MUST be documented.

Rationale: most users adopt the engine embedded and grow into the server; keeping capabilities
optional protects that path, keeps the package surface small and keeps releases auditable.

## Security and Deployment Constraints

- Every server endpoint MUST speak HTTPS with TLS 1.2 or newer. A plain-HTTP endpoint MUST NOT ship
  in any configuration, including samples and smoke profiles.
- Data at rest MUST be encrypted with AES-256-GCM, and key material MUST NOT be logged, committed or
  embedded in test fixtures.
- The server MUST host multiple user databases plus system databases behind the same authenticated
  surface.
- Supported platforms are Windows, Linux and macOS. This line targets `net11.0` with the SDK pinned in
  `global.json`; the pin MUST NOT be changed.
- Machine-local credentials and configuration overrides MUST remain untracked.

Rationale: server mode is only as trustworthy as its weakest transport and key-handling path, and a
downgrade in either is a security regression even when every functional test stays green.

## Development Workflow and Quality Gates

- Spec-Driven Development is the required workflow: `/speckit-constitution` → `/speckit-specify` →
  `/speckit-plan` → `/speckit-tasks` → `/speckit-implement`, with `/speckit-clarify`,
  `/speckit-analyze` and `/speckit-checklist` as optional review gates. A plan MUST NOT proceed past
  its constitution check with unresolved violations.
- `dotnet test` does not work in this repository. Tests MUST run through the Microsoft.Testing.Platform
  executable, for example
  `tests/SharpCoreDB.Tests/bin/Release/net11.0/SharpCoreDB.Tests.exe -filterVSTest 'FullyQualifiedName~MyTestClass'`,
  after `dotnet build tests/SharpCoreDB.Tests/SharpCoreDB.Tests.csproj -c Release -f net11.0`.
- CI restores and builds `SharpCoreDB.CI.slnf` with `--configfile NuGet.Config /p:UseLocalProjectReferences=true`.
  That solution filter is the build contract, not the `.sln`.
- Coverage MUST stay above the 18 % project floor, and at least 50 % of new lines in a pull request
  MUST be covered.
- All documentation, specifications, plans, READMEs and code comments MUST be written in English.
- Performance work MUST follow `docs/performance/AUTONOMOUS_AGENT_BRIEF.md`; its plan is the single
  source of truth and `docs/performance/WORKLOG.md` is append-only, resumed from the last `NEXT:` line.
- SIMD code MUST satisfy `.github/SIMD_STANDARDS.md`: `System.Runtime.Intrinsics` only, never
  `System.Numerics.Vector<T>`.
- The roughly 299 warnings a clean build emits MUST NOT be chased as part of unrelated work.
- Every change MUST state which principles it touches and how it satisfies them.

Rationale: these gates encode lessons that cost real sessions, and they are only effective when they
run by default rather than when someone remembers them.

## Governance

- **Supremacy**: this constitution supersedes conflicting practice. `AGENTS.md`, the performance brief
  and the `.clinerules/` files are runtime guidance subordinate to it; a conflict MUST be resolved in
  the constitution's favour, and the conflicting document amended in the same change.
- **Amendments**: changes are proposed through `/speckit-constitution`, approved by the owner, and
  recorded with a Sync Impact Report covering the version change, modified principles, added and
  removed sections, and deferred items. That report is scratch material for review and MUST be
  removed before the amendment is committed.
- **Versioning**: semantic versioning applies. MAJOR for a backward-incompatible governance change, a
  principle removal or a redefinition. MINOR for a new principle or materially expanded guidance.
  PATCH for clarifications, wording and non-semantic refinements.
- **Compliance review**: every pull request and review MUST verify these principles, and a violation
  blocks merge. Any deviation MUST be justified in the plan's Complexity Tracking section and
  approved by the owner.
- **Owner-only actions**: merging into `master` or `release/*`, packing or publishing packages,
  changing `global.json`, changing release or version metadata, recording a performance baseline, and
  deleting worklog history. The agent may push feature branches, open and update pull requests, and
  keep them current.
- **Immutable history**: no force-push, no history rewrite, and no deletion of
  `docs/performance/WORKLOG.md`.
- **Visible change**: user-visible amendments MUST be recorded in `docs/CHANGELOG.md`.

**Version**: 1.0.0 | **Ratified**: 2026-10-20 | **Last Amended**: 2026-10-20
