# NuGet Publishing - Advanced Configuration & Troubleshooting

## 🎯 Publishing Strategies

### Strategy 1: Automatic Publishing (RECOMMENDED - Current Setup)

**Best for**: Continuous delivery, rapid releases

```yaml
on:
  push:
    branches: [ master ]
```

**Behavior**: 
- Every push to `master` that passes tests → auto-publish
- Great for bug fixes and patches
- Requires discipline with version numbers

**Pros**: Fastest to market, minimal manual steps  
**Cons**: No gate before publishing

---

### Strategy 2: Tag-Based Publishing

**Best for**: Production releases, semantic versioning

```yaml
on:
  push:
    tags: ['v*']
```

**Usage**:
```bash
git tag v1.7.0
git push origin v1.7.0
# Publishes only version 1.7.0
```

**Pros**: Clear release control, easy to identify versions  
**Cons**: Requires manual tagging

---

### Strategy 3: Release-Based Publishing

**Best for**: Stable, well-tested releases

```yaml
on:
  release:
    types: [published]
```

**Usage**:
1. Create GitHub Release manually
2. Tag gets created automatically
3. Publishing triggered by release

**Pros**: Most controlled, release notes included  
**Cons**: Most manual steps

---

### Strategy 4: Scheduled Publishing

**Best for**: Nightly builds, weekly releases

```yaml
on:
  schedule:
    - cron: '0 2 * * 0'  # Weekly Sunday 2 AM
```

**Pros**: Regular, predictable releases  
**Cons**: Publishes regardless of changes

---

## 🔧 Advanced Configurations

### Multi-Feed Publishing

Publish to both NuGet.org and GitHub Packages:

```yaml
- name: Publish to NuGet.org
  run: dotnet nuget push ./artifacts/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate

- name: Publish to GitHub Packages
  run: dotnet nuget push ./artifacts/*.nupkg --api-key ${{ secrets.GITHUB_TOKEN }} --source https://nuget.pkg.github.com/MPCoreDeveloper/index.json --skip-duplicate
```

### Selective Package Publishing

Only publish specific packages:

```yaml
- name: Publish Core Package
  run: dotnet nuget push ./artifacts/SharpCoreDB.*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate

- name: Publish Extensions Package
  run: dotnet nuget push ./artifacts/SharpCoreDB.Extensions.*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate
```

### Conditional Publishing Based on Version

```yaml
- name: Determine if Pre-release
  id: version
  run: |
    VERSION=$(grep '<Version>' src/SharpCoreDB/SharpCoreDB.csproj | grep -oP '>\K[^<]+')
    echo "version=$VERSION" >> $GITHUB_OUTPUT
    if [[ $VERSION == *"-"* ]]; then
      echo "is_prerelease=true" >> $GITHUB_OUTPUT
    else
      echo "is_prerelease=false" >> $GITHUB_OUTPUT
    fi

- name: Publish
  if: steps.version.outputs.is_prerelease == 'false'
  run: dotnet nuget push ./artifacts/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json
```

### Signing Packages

For security, sign packages before publishing:

```yaml
- name: Sign Packages
  run: dotnet nuget sign ./artifacts/*.nupkg --certificate-path ${{ secrets.CERTIFICATE_PATH }} --certificate-password ${{ secrets.CERTIFICATE_PASSWORD }}

- name: Publish Signed Packages
  run: dotnet nuget push ./artifacts/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate
```

---

## 🐛 Troubleshooting Guide

### Error: "401 Unauthorized"

**Symptoms**: 
```
error: Response status code does not indicate success: '401' (Unauthorized).
```

**Causes**:
1. Invalid API key
2. API key not set in secrets
3. API key expired
4. Wrong source URL

**Solutions**:
```bash
# 1. Verify secret exists
gh secret list

# 2. Check API key on NuGet.org
# Go to https://www.nuget.org/account/apikeys

# 3. Update secret
gh secret set NUGET_API_KEY -b "$(cat new-api-key.txt)"

# 4. Verify source URL is correct
# Should be: https://api.nuget.org/v3/index.json
```

---

### Error: "403 Forbidden"

**Symptoms**:
```
error: The specified API key doesn't have permission to push packages.
```

**Causes**:
1. API key doesn't have "Push packages" permission
2. API key scoped to different packages
3. Account doesn't own the package

**Solutions**:
```bash
# 1. Verify API key permissions
# Go to https://www.nuget.org/account/apikeys
# Check "Select Scopes": Push new packages and package versions ✓

# 2. Create new API key with correct permissions
# Remove old secret
gh secret delete NUGET_API_KEY

# 3. Add new secret
gh secret set NUGET_API_KEY -b "new-key-with-permissions"
```

---

### Error: "Package already exists"

**Symptoms**:
```
error: Response status code does not indicate success: '409' (Conflict).
```

**Causes**:
- Publishing same version twice
- Version already published to NuGet

**Solutions**:
```bash
# Option 1: Use --skip-duplicate flag (already in workflow)
dotnet nuget push ./artifacts/*.nupkg --skip-duplicate --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json

# Option 2: Increment version
# Edit .csproj file
# <Version>1.7.1</Version>
# Then republish

# Option 3: Verify version hasn't been published
# Check https://www.nuget.org/packages/SharpCoreDB/
```

---

### Error: "No packages found"

**Symptoms**:
```
Pushing *.nupkg to 'https://api.nuget.org/v3/index.json'...
No files matched the specified pattern.
```

**Causes**:
1. Pack job didn't run or failed
2. Artifacts not found
3. Wrong path in publish step

**Solutions**:
```bash
# 1. Check build/pack job logs
# Look for errors in pack job

# 2. Verify pack output
# Check that ./artifacts/ directory exists
# Run locally: dotnet pack SharpCoreDB.CI.slnf --configuration Release --output ./artifacts

# 3. Check workflow file paths
# Ensure pack and publish steps use same artifact directory
```

---

### Error: "Cannot download package"

**Symptoms**: NuGet restore fails after publishing

**Causes**:
1. Package not yet indexed on NuGet (takes 1-2 minutes)
2. Package deleted before indexing complete
3. Network issue

**Solutions**:
```bash
# Wait 1-2 minutes for indexing
sleep 120

# Try restore again
dotnet restore

# Or specify exact time with cache-bust
dotnet remove package SharpCoreDB
dotnet add package SharpCoreDB@1.7.0
```

---

## 📊 Monitoring & Analytics

### View Publishing History

**GitHub Actions**:
1. Go to **Actions** tab
2. Filter by "publish"
3. Click run to see logs

**NuGet.org**:
1. Go to https://www.nuget.org/packages/SharpCoreDB/
2. Click **Version History**
3. View all published versions with dates

### Monitor Package Downloads

```bash
# View NuGet.org package page for statistics
# Go to: https://www.nuget.org/packages/SharpCoreDB/

# Or use NuGet API
curl https://api.nuget.org/v3-flatcontainer/sharpcoredb/index.json | jq '.versions | length'
```

### Create GitHub Release Notes

```bash
# Automated release notes from git
gh release create v1.7.0 \
  --title "SharpCoreDB v1.7.0" \
  --notes "New features and bug fixes" \
  --draft=false
```

---

## 🔄 Version Management

### Semantic Versioning

Follow [semver.org](https://semver.org/):

```
MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD]

1.7.0          = Stable release
1.7.1          = Patch (bug fix)
1.8.0          = Minor (new feature, backward compatible)
2.0.0          = Major (breaking change)
1.8.0-beta.1   = Pre-release (not auto-installed)
1.8.0-rc.1     = Release candidate
```

### Update Versions in Projects

**Option 1: Manual Update**
```bash
# Edit each .csproj file
# Find: <Version>1.7.0</Version>
# Change to: <Version>1.8.0</Version>
```

**Option 2: Script Update**
```powershell
# PowerShell: Update all .csproj files
$version = "1.8.0"
Get-ChildItem -Recurse -Filter "*.csproj" | 
  ForEach-Object {
    (Get-Content $_) -replace '<Version>.*?</Version>', "<Version>$version</Version>" | 
    Set-Content $_
  }
```

**Option 3: MinVer (Automatic)**
```xml
<PropertyGroup>
  <MinVerSkip>false</MinVerSkip>
</PropertyGroup>
```

---

## 🚀 Performance Optimization

### Parallel Publishing

For multiple feeds:
```yaml
- name: Publish to Multiple Feeds
  run: |
    dotnet nuget push ./artifacts/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate &
    dotnet nuget push ./artifacts/*.nupkg --api-key ${{ secrets.GITHUB_TOKEN }} --source https://nuget.pkg.github.com/MPCoreDeveloper/index.json --skip-duplicate &
    wait
```

### Cache Dependencies

```yaml
- name: Setup .NET 10
  uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '10.0.x'

- name: Cache NuGet packages
  uses: actions/cache@v4
  with:
    path: ~/.nuget/packages
    key: ${{ runner.os }}-nuget-${{ hashFiles('**/packages.lock.json') }}
    restore-keys: |
      ${{ runner.os }}-nuget-
```

---

## 🔐 Security Best Practices

### API Key Rotation
1. Set expiration dates (annually)
2. Create new key before old one expires
3. Update GitHub secret
4. Delete old key from NuGet

### Access Control
- [ ] Only admins can manage `NUGET_API_KEY` secret
- [ ] Review repository access regularly
- [ ] Use branch protection rules

### Audit Logging
```bash
# Monitor who published what
gh api repos/MPCoreDeveloper/SharpCoreDB/actions/runs \
  --jq '.workflow_runs[] | {name: .name, created_at: .created_at, actor: .actor.login}'
```

---

## 📋 Maintenance Checklist

### Daily
- [ ] Monitor GitHub Actions for failed publishes
- [ ] Check for dependency vulnerabilities

### Weekly
- [ ] Review NuGet package downloads
- [ ] Check for new issues from users

### Monthly
- [ ] Update dependencies
- [ ] Review code coverage
- [ ] Plan next release

### Quarterly
- [ ] Check API key expiration
- [ ] Review security settings
- [ ] Update documentation

### Yearly
- [ ] Rotate NuGet API key
- [ ] Review and update all policies
- [ ] Plan major version releases

---

## 📚 Additional Resources

- [NuGet Docs: Publishing](https://docs.microsoft.com/nuget/nuget-org/publish-a-package)
- [dotnet nuget push](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push)
- [GitHub Actions](https://docs.github.com/actions)
- [Semantic Versioning](https://semver.org/)
- [MinVer - Auto Versioning](https://github.com/adamralph/minver)

---

**Created**: 2025-01-28  
**Last Updated**: 2025-01-28
