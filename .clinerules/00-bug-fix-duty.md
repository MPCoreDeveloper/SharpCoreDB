# Found bugs are fixed, not filed (owner directive, 2026-09-26)

Applies to every session and every file — performance work included. No `paths:` frontmatter on
purpose: this rule is always in force.

- **A defect you find is yours to fix in the same session.** Do not end a session with a finding
  parked as a note, a `TODO`, a worklog bullet or a hand-off to the next session.
- Minimum bar for "fixed": the wrong behaviour is reproduced by a test that **fails before and
  passes after**, the build and the core suite are green, and the fix is recorded — in the worklog
  for this plan's work, in `docs/CHANGELOG.md` when the behaviour is user-visible.
- **Fix the cause, not the symptom.** Read the code that produces the wrong number before choosing
  the fix — never fix an inferred cause. If the same logic is duplicated, fix **every** copy (one
  resolver for five call sites, not five patches).
- Run the **wider** suite, not only the new tests: twice now the suite caught a regression the
  focused tests had missed. Extend the tests for whatever it caught.
- Legitimate reasons to hand a bug on instead: it breaks a safety rail (brief §11), it needs the
  owner's decision, or it is outside this repository. Then log it **with evidence**, mark it
  `BLOCKED`, and keep it in the worklog's `NEXT:` line — never delete it.
- Never "fix" a defect by weakening what the code guarantees: no disabling encryption, no trading
  durability, no loosening a test until it passes.
- Worked example (session 62, the directive's origin): `DOUBLE`/`FLOAT`/`INT` columns were silently
  TEXT because one type map was copied into five places, and a client disconnect was logged as an
  authentication failure. Both were fixed in the session that found them, each with a red→green test
  and a live check — see `docs/performance/WORKLOG.md`, session 62.
