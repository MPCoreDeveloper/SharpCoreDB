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

### Scenario B: Manual Publishing (Optional)

If you prefer to manually trigger publishing, create a separate workflow file `.github/workflows/publish-manual.yml`:

```yaml
name: Manual NuGet Publish

on:
  workflow_dispatch:
    inputs:
      version:
        description: 'Package version to publish'
        required: false

jobs:
  publish:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet pack SharpCoreDB.CI.slnf --configuration Release --output ./artifacts
      - run: dotnet nuget push ./artifacts/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate
```

### Scenario C: Tag-Based Release Publishing

To publish only on git tags (recommended for production):

```yaml
on:
  push:
    tags:
      - 'v*'
```

## Version Management

Your projects use `<Version>` tags in `.csproj` files. Ensure each project file has:

```xml
<PropertyGroup>
  <Version>1.7.0</Version>
  <AssemblyVersion>1.7.0</AssemblyVersion>
  <FileVersion>1.7.0</FileVersion>
</PropertyGroup>
```

### Automated Versioning (Optional)

To automatically version based on git commits, use:

```xml
<PropertyGroup>
  <Version>1.7.0</Version>
  <InformationalVersion>1.7.0+$(GitCommitHash)</InformationalVersion>
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

For beta/preview versions, use:
```xml
<Version>1.7.0-beta.1</Version>
```

Pre-release packages won't be automatically installed by default but are searchable on NuGet.org.

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
