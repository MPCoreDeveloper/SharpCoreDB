# NuGet publishing

SharpCoreDB publishes to NuGet.org through **nuget.org trusted publishing** — there is no long-lived API
key on the net11 line any more.

## How a release is published

```
Push to master
    ↓
CI/CD Pipeline (ci.yml): Build & Test on ubuntu/windows/macos → Pack → uploads the `nuget-packages` artifact
    ↓
Publish to NuGet.org (publish.yml): runs after a green CI run, downloads that artifact,
    exchanges its OIDC token for a one-hour key, and pushes only the packages the feed does not have
```

- **The pushing job lives in `.github/workflows/publish.yml` and nowhere else.** The nuget.org policy
  `SharpCoreDBPolicy` is bound to this repository **and to that file name**; `NuGet/login@v1` is refused
  when it runs from a different workflow file. Moving the job back into `ci.yml` breaks publishing.
- The job requests `id-token: write` (the OIDC token) and `actions: read` (downloading the CI artifact).
- The account name passed to `NuGet/login` is the repository variable **`NUGET_USER`**, defaulting to
  `MPCoreDeveloper`. It is the nuget.org *user name* — not the policy name, and not a secret.
- `pack` stays in `ci.yml` and runs only on a push to `master`; `publish.yml` reuses that artifact so
  nothing is built or tested twice.

## Manual publishing

Run the **Publish to NuGet.org** workflow (`workflow_dispatch`):

- with **no input**, it reuses the newest successful `ci.yml` run on `master` (so a re-run after a failed
  publish pushes exactly the packages that run packed);
- with an explicit **`version`** (e.g. `2.1.4`), it packs the `SharpCoreDB.CI.slnf` itself with
  `/p:Version=<version>` and publishes that.

The former `publish-manual.yml` was removed with the move to trusted publishing: its job — pack with an
explicit version or suffix and push in dependency order — is covered by the `version` input.

## Why the push is selective

NuGet.org rate-limits pushes and answers `403 Quota Exceeded` once the budget is gone, and
`--skip-duplicate` does not avoid the charge: a duplicate is still a PUT. `publish.yml` therefore asks the
flat-container feed what exists and pushes only what is missing, in dependency order (core → dependents →
mid-level → top-level → catch-all), waiting 30 s for indexing between layers that actually pushed.

## What is deliberately not used

- **No `NUGET_API_KEY` secret.** The key it held expired on 2026-11-12 and turned master's publish job red
  (`403: The specified API key has expired`). Do not reintroduce a static key for this line.
- **`.github/workflows/ci-net10.yml`** (the parked net10.0 line on `release/v2.0`) still uses the shared
  `NUGET_API_KEY`. That line only runs on `release/v2.0`/`v2.0*` tags, so it is dormant; if 2.0.x servicing
  resumes, it needs its own trusted-publishing policy or a fresh key.
- **`src/SharpCoreDB/.github/workflows/nuget-build.yml`** is a legacy nested workflow (not a root workflow,
  so GitHub never runs it). It is kept only as history.

## Troubleshooting

| Symptom | Cause |
|---|---|
| `403 The specified API key has expired` | A static key is being used somewhere; only `publish.yml` should push, and it uses the OIDC exchange. |
| `NuGet/login` fails with a policy mismatch | The job was moved out of `publish.yml`, or the workflow file was renamed; the policy is bound to the file name. |
| `NuGet/login` fails with an unknown user | `NUGET_USER` is missing or is not the nuget.org account name (it is not the policy name). |
| Publish job skipped | The triggering `CI/CD Pipeline` run did not succeed (`if:` requires `conclusion == 'success'`). |
| Nothing pushed, summary says so | Every package in the artifact is already on the feed at that version — bump `SharpCoreDBVersion`, or dispatch with `version`. |
