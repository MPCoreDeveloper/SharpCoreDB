# Benchmark baselines

`dual-mode-baseline.json` is the reference the `--gate` mode compares against — the §2.4 regression
gate, run by [`.github/workflows/benchmarks.yml`](../../../.github/workflows/benchmarks.yml).

```bash
# Compare against the committed baseline (exit 0 = ok, 1 = regressed, 2 = too noisy to conclude)
dotnet run -c Release -- --gate

# A different tolerance, or a different baseline file
dotnet run -c Release -- --gate --gate-factor=2.0
dotnet run -c Release -- --gate --gate-baseline=results/dual-mode-20260915_172539.json

# Re-record the committed baseline
dotnet run -c Release -- --write-baseline
```

## Why this exists

A write-path regression has landed silently twice on this branch. UPDATE drifted between v2.0 and v2.1
inside the documented ±20% noise band with nothing to catch it, and during the v2.1 deferred-DELETE work
a reconcile placed at `Table.Flush()` made random-key DELETE **4× slower** (294,185 → 70,248 ops/sec)
while all 2,321 tests stayed green. Neither showed up anywhere except a hand-run of this harness.

## Reading a result

The gate uses the §2 protocol: the same two arms as `--dual-mode` (`raw` = `NoEncryptMode=true`,
`default` = the product default), alternated per rep, medians over three reps.

- **`ratio` is `baseline ÷ current`, so >1 means slower.** `watch` marks a 1.25×–1.5× slowdown that
  passes but is worth noting — two of those in a row is how a regression arrives without ever tripping
  the gate.
- **The rep spread is printed before the verdict.** It is the run's own noise (max ÷ min across reps,
  per metric). Above 2.5× the gate returns exit 2 and concludes nothing, because such a run measures the
  machine's load rather than the code. A clean run on a quiet machine sits near 1.0; DELETE on the
  random-key workload is the noisiest metric even then.
- **A `REGRESSED` verdict does not mean "revert".** It means "re-run on a quiet machine". The tolerance
  is generous on purpose: the band is ±20%, so a factor near 1 would fire on noise, while 1.5× still
  catches the ≥2× regressions this gate exists for.

## The baseline is machine-specific

This file was recorded on the maintainer's machine. Absolute ops/sec do not transfer between machines —
GitHub-hosted runners get a different CPU on every run — so on hosted hardware the gate is trend
evidence, not a verdict. For an answer that means something, run it on a fixed/self-hosted machine, and
re-record the baseline there with `--write-baseline`.

Re-recording is an explicit, reviewable act and never automatic — a baseline recorded *during* a
regression silently accepts that regression for every run that follows. When you do re-record, put the
reason in the commit message.
