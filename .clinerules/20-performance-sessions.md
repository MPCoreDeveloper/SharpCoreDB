---
paths:
  - "docs/performance/**"
  - "tests/benchmarks/**"
  - "tests/SharpCoreDB.Benchmarks/**"
  - "src/SharpCoreDB/Storage/**"
  - "src/SharpCoreDB/Services/Storage*"
---
# Unattended performance sessions

These are additive to `AGENTS.md` (Cline reads it natively) — that file stays authoritative for policy
and for the measurement pitfalls. This file only carries the session protocol, which is not in it.

- Read `docs/performance/AUTONOMOUS_AGENT_BRIEF.md` in full before the first edit; for this work it
  overrides other guidance.
- Append only to the worklog. Keep the tokens `Session:`, `Verdict:` and `NEXT:` on their own lines,
  exactly as written; the owner greps for them.
- In an unattended run: never ask a question, never pause for approval, never stop to share status.
  Decide, write the decision and its reason to the worklog, continue.
- The brief's §5 timeboxes are enforced by the per-item session count — state it in every entry.
- Blocked or refuted: log the finding with evidence, mark the item `BLOCKED` or `REJECTED`, move to the
  next item. Do not spin on it.
- Keep build, the core test suite and `--gate` green. A gate failure gets a documented re-run, never a
  silent retry.
- **Bugs found while measuring are fixed, not written down** — in the same session, with a red→green
  test, the wider suite run, and the evidence in the worklog. See `00-bug-fix-duty.md` (owner
  directive, 2026-09-26). Only a safety-rail conflict, an owner decision or something outside this
  repository may be handed on, and then it is `BLOCKED` in the worklog, never a bare note.
