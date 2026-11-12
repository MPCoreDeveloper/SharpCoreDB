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

## Known measurement defect: the UPDATE cell picks a mode (open since 2026-11-12)

The gate's **`raw UPDATE`** arm lands in one of two modes — roughly **215.000** or **420.000 ops/sec**
(1,86–1,95× apart) — and then holds it. Sampled on the GA runtime (`11.0.100`) on 2026-11-12 at the default
8 discarded warm-up passes: rep spreads of **2,39× / 2,55× / 3,20×**, with the fast mode appearing in some
reps and not others. At **16** passes: one fast run (`396.743 / 426.584 / 429.214`, spread 1,08×) and one
slow run (`217.093 / 225.053 / 264.700`, spread 1,22×) — so a deeper warm-up makes the reps agree with each
other and still lets the process disagree with the recorded baseline. `raw READ` has a milder version of the
same split (139.000 vs ~200.000).

**What this means for a baseline.** A freshly recorded reference is a coin flip for those cells: the next
run has a real chance of disagreeing by ~1,9× on an unchanged binary. On 2026-11-12 a baseline recorded at
09:23Z failed the gate at 09:5x with `raw UPDATE 2,05×` and then came out `INCONCLUSIVE` (2,55×) on the
re-run — nothing was rebuilt in between. Until the switch behind the mode is found, read a `REGRESSED` on
`raw UPDATE` / `raw READ` as **"wrong mode"**, not as a regression, and confirm it with a re-run before
acting.

**What has been ruled out (session 87 of the worklog has the numbers).** Machine load: `INSERT` and `READ` inside those
same reps stayed within 1,07×/1,08×, and the new `SHARPCOREDB_PHASE_DIAG=1` line (wall vs process CPU vs GC counts/pause
per phase) shows the *slow* passes burning **more** CPU than the fast ones — CPU/wall ≥ 1, so the thread is not starved.
Warm-up depth on its own: both modes reproduced at 16 passes. GC frequency and GC mode: a 2 MB gen0 kept the fast mode
reachable, and `DOTNET_gcServer=1` kept the split (at higher absolute numbers). Tiering: `DOTNET_TieredCompilation=0`
reproduced the split too. **Hash-index maintenance:** `SHARPCOREDB_HASH_INDEXES=0` reproduced it as well (2,61×). The
workload data: the statement lists are fixed and prebuilt. The profiler's signature — **exactly one stage blowing up per
slow pass, a different one each time** (engine-write 54,1 ms against 1,9–6,3; row-locate-index 38,4/27,7 against 4–8;
in-place-patch 24,1 against 1,1–5,4) — is recorded there for whoever picks this up.

## The UPDATE cell's mode is a per-rep disturbance — the same code, read with the wrong statistic (2026-11-12)

Measured on the GA runtime, same binary, two consecutive gate runs (the new `best` column prints the fastest of
the three reps next to the median the verdict uses):

| metric | run 1 median | run 1 best | run 2 median | run 2 best |
|---|---|---|---|---|
| raw INSERT | 209.866 | 213.675 | 223.954 | 225.234 |
| **raw UPDATE** | 251.636 | **390.672** | 247.158 | **432.851** |
| default INSERT | 183.422 | 188.285 | 186.334 | 187.020 |
| **default UPDATE** | 180.963 | **301.913** | 201.231 | **304.723** |

Inside *every* run the fastest rep is **50–70 % above the median** on UPDATE, and `default UPDATE`'s best value
agrees across the two runs to **1 %** (301.913 vs 304.723) where the medians differ by 11 %. The consequence is the
whole finding: the "mode" is **not a different code path that a run selects** — it is a per-rep disturbance landing
in a 22–35 ms window (a ~50 ms GC pause appears in ~4 % of measured phases; most slow passes carry one unexplained
single-stage stall of 20–55 ms whose stage varies). The code's capability is the *undisturbed* rep.

**What that means for the guard — implemented 2026-11-12.** `--write-baseline` now records **both** statistics
(`raw`/`default` = the median of each arm's reps; `rawBest`/`defaultBest` = its fastest rep), and when a baseline
carries the capability entries the gate compares **best-to-best** and that comparison decides the verdict. Two
ordering rules changed with it, each replacing an accidental behaviour with an explicit one:

- **Recording is no longer blocked by the rep spread.** It used to be, by accident of ordering, which made the cell
  unrecordable on any box with a disturbance in the UPDATE window (every attempt exited 2 with 2,63×–3,29× spreads).
  A recording is not a verdict. The spread is still printed, loudly.
- **Recording refuses a materially worse capability.** If any metric's best rep comes out more than 25 % below the
  best recorded on disk, nothing is written and the gate exits non-zero naming the metric — the protection the old
  ordering was standing in for, and a stronger one, because a regression lowers every rep while a disturbance lowers
  only one.
- **The spread gate still decides the median verdict**, and for a baseline recorded before this change (medians only)
  the median comparison still decides, exactly as before. Old baselines keep working; they just cannot be read as
  capability.

First run under the new protocol (`2026-11-12T10:29Z` baseline): medians spread 3,25× → "no median verdict", then
capability 8/8 `ok`, worst cell `raw UPDATE` 1,19× (477.824 → 399.960) → **`GATE PASSED` (exit 0)**. That is the first
UPDATE verdict on this box that a disturbance cannot flip.


This file was recorded on the maintainer's machine. Absolute ops/sec do not transfer between machines —
GitHub-hosted runners get a different CPU on every run — so on hosted hardware the gate is trend
evidence, not a verdict. For an answer that means something, run it on a fixed/self-hosted machine, and
re-record the baseline there with `--write-baseline`.

Re-recording is an explicit, reviewable act and never automatic — a baseline recorded *during* a
regression silently accepts that regression for every run that follows. When you do re-record, put the
reason in the commit message.
