# An issue closes only when its own case has been re-run and passes (owner directive, 2026-11-13)

Applies to every session, every issue and every repository these rules are loaded in. No `paths:`
frontmatter on purpose: this rule is always in force. It is additive to `00-bug-fix-duty.md` — that file
is about *fixing* a defect, this one is about *closing the report*.

## The closing gate

An issue may be marked solved only when **all four** of these hold:

1. **The reporter's own case has been run.** Their snippet, their paths, their connection string, their
   package version — not a paraphrase and not "the same shape". Run it against the artifact they would
   actually install (the published packages) or against the release commit that carries the fix.
   "It compiles", "the unit test passes" and "fixed in master" are **not** a test of their case.
2. **It passes *now*, in the current state of the code.** A pass from an earlier session, an assumption
   about a fix that has not shipped, or a cached expectation is worth nothing: execute it in this session
   and read the result. A report usually has more than one code path, and the *second* path is where the
   second defect hides — the 2026-11-13 example below is exactly that.
3. **A regression test named for the issue exists in the repository**, at the layer that owns the defect.
   See "Regression test" below.
4. **The closing comment carries the evidence**: what was executed (command, package version, runtime),
   the observed result, the test that pins it, and — when part of the report is *not* fixed — precisely
   which part, why, and when it ships. Credit the reporter in `CONTRIBUTORS.md` when their reproduction
   produced the fix.

If any of the four is missing, the issue stays **open**. When the fix exists only on `master` and has not
been released, say so in a comment and leave the issue open: the owner's release cadence is not the
reporter's confirmation.

## Regression test: one per issue, at the owning layer

- Add a test that **would have failed before the fix**. Name it for the behaviour, never for the reporter:
  `CreateTable_QuotedTableName_IsResolvableByUnquotedStatements`, not `CastleSoftTest`. Name the issue in
  the doc comment and state the case it pins.
- Put it where the defect lives. A defect surfacing through an adapter is still an **engine** defect when
  the parser is wrong: pin the engine behaviour directly **and** keep one end-to-end test of the
  reporter's call shape. On 2026-11-13 those two caught different things — the adapter test caught the
  first defect, the engine-level matrix found the second.
- Cover the reporter's **whole** reproduction, every branch — both connection strings, both file
  extensions, both quoting styles, WITH and WITHOUT a password. Half a reproduction hides half the bugs.
- **Prove it red.** Stash or revert only the `src` change, run the new tests, record the failure count,
  then restore and re-run. A test that has never been seen failing proves nothing (2026-11-13: 9 of 11 new
  cases failed on the previous DDL parser; 5 of 6 before the first fix).
- Then run the wider suite, as `00-bug-fix-duty.md` requires, and record both numbers.

## Worked example — issue #497 (2026-11-13)

The report: `SharpCoreDB.Functional.Linq2DB` "stores data in SQLite, not SharpCoreDB and can't use
encryption". The adapter fix shipped in `2.1.3`; the issue was answered and closed. Re-running the
reporter's own snippet against the **published 2.1.3 packages** in the same session showed that their
*second* path — `Data Source=probe.db`, no `.scdb` extension, so directory storage — still failed:
quoted identifiers in `CREATE TABLE` were catalogued with their quoting, so `CreateTableAsync` succeeded
and `InsertAsync` threw `Table notes does not exist`, and then `ArgumentOutOfRangeException` from
`table.ColumnTypes[-1]` once the table name resolved. Two further defects, fixed in `34c189c4` and pinned
by `QuotedIdentifierDdlTests` (11 cases, 9 red before) plus the reporter's no-password case in
`StorageTests`.

The mechanical lesson: **the closing comment names the version that was actually executed, and the case is
executed, never assumed.** "The fix is in" is a statement about a commit; "the case passes" is a statement
about a release, and only the second one closes an issue.
