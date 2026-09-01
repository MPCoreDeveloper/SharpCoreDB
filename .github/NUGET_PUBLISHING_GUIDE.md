# NuGet Package Publishing Automation Guide

## Overview

Your SharpCoreDB project now has **fully automated NuGet package publishing** through GitHub Actions. Packages are automatically published to NuGet.org on every successful push to the `master` branch.

## How It Works

### Workflow Jobs

1. **build** - Runs tests on all platforms (Windows, Linux, macOS)
2. **pack** - Creates NuGet packages (`.nupkg` files)
3. **publish** - Publishes packages to NuGet.org (NEW)

```
Push to master
    ↓
Build & Test (multiple OS)
    ↓
Pack NuGet Packages
    ↓
Publish to NuGet.org
```

## Setup Required

### 1. Create NuGet API Key

1. Go to https://www.nuget.org/account/apikeys
2. Create a new API key with:
   - **Key type**: Push new packages and package versions
   - **Scope**: All packages (or specific packages if preferred)
   - **Expiration**: Set appropriately (e.g., 1 year)

### 2. Add Secret to GitHub Repository

1. Navigate to: **GitHub Repository → Settings → Secrets and variables → Actions**
2. Click **New repository secret**
3. Configure:
   - **Name**: `NUGET_API_KEY`
   - **Value**: Paste your API key from step 1

## Publishing Scenarios

### Scenario A: Automatic Publishing (Current Setup)

Every push to `master` that passes all tests will automatically:
1. Build packages in Release mode
2. Publish all `.nupkg` files to NuGet.org
3. Skip duplicate versions automatically

**When to use**: For CI/CD continuous delivery

### Scenario B: Manual Publishing (Current Setup)

The repo ships with `.github/workflows/publish-manual.yml` (**"Manual NuGet Publish"**), triggerable from the
Actions tab with a **`workflow_dispatch`** input. It builds and pushes all packages to NuGet.org in dependency
order (with `--skip-duplicate`):

| Input | Description |
|---|---|
| `reason` | Free-text note shown in the run summary |
| `versionSuffix` | Optional NuGet pre-release suffix. **Empty** → stable version from the `.csproj` files (e.g. `2.0.0.0`). **`preview.1`**, **`rc.1`**, **`beta.1`** → `2.0.0.0-preview.1` etc. |

### Scenario B2: Publishing the 2.0.0.0 release (click-by-click)

1. Go to **https://github.com/MPCoreDeveloper/SharpCoreDB → Actions** (tab on top)
2. In the left sidebar, select **"Manual NuGet Publish"**
3. Click the blue **"Run workflow"** button (top right)
4. In the dialog:
   - **Branch**: `master` (the v2.0.0.0 trunk — SharpCoreDB v2 is now the leading line)
   - **versionSuffix**: leave **empty** for the stable `2.0.0.0` release
   - **reason**: e.g. `2.0.0.0 – performance-first V2 release`
5. Click **"Run workflow"**
6. Open the run to watch it: the **Publish** job logs show `↗ Pushing <package>` per `.nupkg`, grouped in
   dependency layers (core → dependents → mid-level → top-level).
7. Verify: **https://www.nuget.org/packages/SharpCoreDB/** — the `2.0.0.0` version should appear.

> **Requires** the `NUGET_API_KEY` repository secret (`Settings → Secrets and variables → Actions`). If it was
> used for the 1.9.7 publish it is already configured.

### Scenario C: Tag-Based Release Publishing

To publish only on git tags (recommended for production):

```yaml
on:
  push:
    tags:
      - 'v*'
```

## Version Management

SharpCoreDB uses **4-part versions** (`n.n.n.n`, e.g. `2.0.0.0`). The versions come from the `<Version>` tags
in `.csproj` files, but the **single source of truth for the SharpCoreDB package family is
`<SharpCoreDBVersion>` in `Directory.Packages.props`** (used by all central package references).

Each packable project file carries the same version:

```xml
<PropertyGroup>
  <Version>2.0.0.0</Version>
  <AssemblyVersion>2.0.0.0</AssemblyVersion>
  <FileVersion>2.0.0.0</FileVersion>
</PropertyGroup>
```

### Automated Versioning (Optional)

To automatically version based on git commits, use:

```xml
<PropertyGroup>
  <Version>2.0.0.0</Version>
  <InformationalVersion>2.0.0.0+$(GitCommitHash)</InformationalVersion>
</PropertyGroup>
```

Or use a tool like [MinVer](https://github.com/adamralph/minver) for semantic versioning from git tags.

## Monitoring Publishes

### View Publishing Results

1. Go to **GitHub Repository → Actions**
2. Look for the workflow run corresponding to your commit
3. Check the **publish** job logs for:
   - ✅ Successful push messages
   - ⚠️ Duplicate package warnings (normal - automatically skipped)
   - ❌ Authentication errors (check `NUGET_API_KEY` secret)

### Verify on NuGet.org

- Navigate to https://www.nuget.org/packages/SharpCoreDB/
- New versions appear within 1-2 minutes after successful publishing

## Troubleshooting

### Issue: "401 Unauthorized" during publish

**Solution**: 
- Verify `NUGET_API_KEY` is correctly set in GitHub Secrets
- Check API key hasn't expired on NuGet.org
- Ensure API key has "Push new packages" permission

### Issue: "Package already exists"

**Solution**: 
- This is expected if version hasn't changed
- The workflow uses `--skip-duplicate` flag to automatically skip
- Change version in `.csproj` to publish a new release

### Issue: Publish job doesn't run

**Solution**:
- Verify push is to `master` branch (not `develop` or PR)
- Check that `build` job passed all tests
- Look for errors in the `build` job first

## Best Practices

### 1. Semantic Versioning

Follow [semver.org](https://semver.org/):
- **Major**: Breaking changes (2.0.0)
- **Minor**: New features (1.1.0)
- **Patch**: Bug fixes (1.0.1)

### 2. Release Notes

Create GitHub Releases for each version:
1. Go to **Releases → New Release**
2. Create tag: `v1.7.0`
3. Add release notes describing changes
4. Publish release

### 3. Pre-release Packages

For pre-releases of the v2.0.0.0 line (e.g. `2.0.0.0-preview.1`), **do not edit the `.csproj` files** — trigger the
"Manual NuGet Publish" workflow with a `versionSuffix` (see Scenario B2). The workflow passes
`/p:Version=2.0.0.0-<suffix>` to the whole solution, so every package and every internal
`ProjectReference` dependency stays consistent on the same pre-release version.

Consumers opt in explicitly — pre-release packages are **not** selected by default:

```bash
dotnet add package SharpCoreDB --version 2.0.0.0-preview.1
```

Or in Visual Studio: **Manage NuGet Packages → tick "Include prerelease"**.

### 4. Package Metadata

Ensure each `.csproj` has:
```xml
<PropertyGroup>
  <PackageId>SharpCoreDB</PackageId>
  <Title>SharpCoreDB - High Performance Database Engine</Title>
  <Description>A .NET 10 high-performance database engine with zero-allocation principles</Description>
  <Authors>MPCoreDeveloper</Authors>
  <PackageProjectUrl>https://github.com/MPCoreDeveloper/SharpCoreDB</PackageProjectUrl>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <RepositoryUrl>https://github.com/MPCoreDeveloper/SharpCoreDB</RepositoryUrl>
  <RepositoryType>git</RepositoryType>
  <PackageTags>database;performance;dotnet10;sharpcoredb</PackageTags>
</PropertyGroup>
```

## Environment Variables

The workflow automatically sets:
- `DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true`
- `DOTNET_CLI_TELEMETRY_OPTOUT=true`
- `DOTNET_NOLOGO=true`

These optimize CI performance and disable telemetry.

## Files Modified

- `.github/workflows/ci.yml` - Added `publish` job and artifact handling

## Next Steps

1. ✅ Add `NUGET_API_KEY` secret to GitHub
2. ✅ Verify version numbers in `.csproj` files
3. ✅ Commit and push changes to `master`
4. ✅ Monitor the GitHub Actions workflow
5. ✅ Verify packages appear on NuGet.org

## Support

For questions:
- Check GitHub Actions logs for detailed errors
- Review [NuGet Publishing Documentation](https://docs.microsoft.com/nuget/nuget-org/publish-a-package)
- Consult [dotnet nuget push](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push) documentation

---

**Created**: 2025-01-28  
**Last Updated**: 2025-01-28
