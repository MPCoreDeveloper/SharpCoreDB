# CI/CD Best Practices for SharpCoreDB Publishing

## 🎯 Core Principles

### 1. Automated Everything
✅ **Builds**, **Tests**, **Packing**, and **Publishing** are fully automated  
❌ No manual `dotnet pack` or `dotnet nuget push` commands needed  

### 2. Fail Fast
- Tests run on all platforms (Windows, Linux, macOS)
- Coverage validation gates publishing
- Vulnerabilities and deprecations are caught early

### 3. Single Source of Truth
- `.csproj` files contain version information
- Git commits map to specific releases
- GitHub Actions logs are the audit trail

### 4. Security First
- Secrets stored encrypted in GitHub
- No credentials in source code
- All actions are logged and traceable

---

## 🔄 Recommended Workflow

### Daily Development

```bash
# 1. Create feature branch
git checkout -b feature/new-feature

# 2. Make changes and test locally
dotnet test tests/SharpCoreDB.Tests

# 3. Commit (DO NOT PUSH YET)
git commit -m "Add new feature"

# 4. Pull latest master
git fetch origin
git rebase origin/master

# 5. Create Pull Request (DO NOT MERGE YET)
git push origin feature/new-feature
# Then create PR on GitHub for review

# 6. Wait for GitHub Actions (automatic build/test)
# Check: Actions tab → see all tests pass

# 7. After approval, merge to master
# GitHub will trigger auto-publishing ✅
```

### Release Management

```bash
# STEP 1: Plan Release
# - Decide version number (semver)
# - Document changes in CHANGELOG.md
# - Create draft release notes

# STEP 2: Update Versions
# Find all .csproj files with versions:
grep -r "<Version>" src/ --include="*.csproj"

# Edit each one:
# OLD: <Version>1.7.0</Version>
# NEW: <Version>1.8.0</Version>

# STEP 3: Test Everything Locally
dotnet clean
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release --no-build

# STEP 4: Commit & Push
git add .
git commit -m "Release v1.8.0"
git push origin master
# 🚀 CI/CD automatically publishes!

# STEP 5: Create GitHub Release
gh release create v1.8.0 \
  --title "SharpCoreDB v1.8.0" \
  --notes "$(cat RELEASE_NOTES.md)"

# STEP 6: Verify on NuGet
# Wait 1-2 minutes, then visit:
# https://www.nuget.org/packages/SharpCoreDB/
```

---

## 📋 Pre-Push Checklist

Before pushing to `master`, verify:

```bash
# 1. Tests pass locally
dotnet test -c Release

# 2. No warnings/errors
dotnet build -c Release /warnaserror

# 3. Code coverage acceptable
dotnet test -c Release --collect:"XPlat Code Coverage"

# 4. No deprecated packages
dotnet list package --deprecated

# 5. No vulnerable packages
dotnet list package --vulnerable

# 6. Version numbers updated consistently
grep -r "<Version>" src/ --include="*.csproj" | head -20

# 7. Commit message is clear
git log -1 --oneline

# 8. No WIP or TODO commits
git show --name-only

# 9. Ready? Push!
git push origin master
```

---

## 🚦 GitHub Actions Status Indicators

### ✅ SUCCESS (Green)
```
workflow run: SUCCESS
├─ build: PASSED ✅ (all platforms)
├─ pack: PASSED ✅
└─ publish: PASSED ✅
  → Packages published to NuGet.org
```

**Action**: Verify packages on NuGet.org, celebrate! 🎉

### ⚠️ BUILD FAILED (Red)
```
workflow run: FAILED
├─ build: FAILED ❌
│  └─ Test SharpCoreDB.Tests: FAILED
│     └─ Error: Test timeout on ubuntu-latest
├─ pack: SKIPPED
└─ publish: SKIPPED
```

**Action**: 
1. Check build logs for errors
2. Fix locally, test thoroughly
3. Commit fix and push again

### ⚠️ PACK FAILED (Red)
```
workflow run: FAILED
├─ build: PASSED ✅
├─ pack: FAILED ❌
│  └─ Error: Missing version in SharpCoreDB.csproj
└─ publish: SKIPPED
```

**Action**:
1. Check `.csproj` files for `<Version>` tags
2. Ensure all have valid semantic versions
3. Commit fix and push again

### ⚠️ PUBLISH FAILED (Red)
```
workflow run: FAILED (CRITICAL)
├─ build: PASSED ✅
├─ pack: PASSED ✅
└─ publish: FAILED ❌
   └─ Error: 401 Unauthorized
```

**Action**:
1. Check if `NUGET_API_KEY` secret is set
2. Verify API key hasn't expired
3. Check NuGet.org API key permissions
4. Update secret and retry (manually)

---

## 🔍 Monitoring & Alerting

### Daily Monitoring

```bash
# Check workflow status
gh api repos/MPCoreDeveloper/SharpCoreDB/actions/runs --jq '.[0:5] | .[] | {name: .name, status: .status, conclusion: .conclusion, created_at: .created_at}'

# Alternative: Just check GitHub Actions tab regularly
```

### Weekly Review

```bash
# Analyze workflow trends
# Questions to ask:
# - Any failed builds this week?
# - Are tests getting slower?
# - Any security warnings?
# - API key expiring soon?

# Check NuGet.org metrics
# - New downloads?
# - Any 1-star reviews indicating bugs?
# - Version adoption patterns?
```

### Monthly Maintenance

```bash
# 1. Check API key expiration
echo "Check: https://www.nuget.org/account/apikeys"

# 2. Review and update documentation
ls -la .github/ | grep NUGET

# 3. Audit GitHub Actions logs
gh api repos/MPCoreDeveloper/SharpCoreDB/actions/runs --paginate

# 4. Plan next releases
# Review open issues and PRs
gh issue list
gh pr list
```

---

## 🚨 Emergency Procedures

### Hotfix Release

**Situation**: Critical bug found in production

```bash
# 1. Create hotfix branch from master
git checkout -b hotfix/critical-bug origin/master

# 2. Fix the bug
# Make minimal changes only!

# 3. Update patch version
# OLD: <Version>1.7.0</Version>
# NEW: <Version>1.7.1</Version>

# 4. Test thoroughly
dotnet test -c Release

# 5. Push (triggers auto-publish)
git push origin hotfix/critical-bug

# 6. Create PR, get quick approval, merge to master
gh pr create --base master
# ... manual approval ...
gh pr merge

# 7. ✅ Hotfix automatically published to NuGet!
```

### Yank Release (Remove from NuGet)

If a published version has critical issues:

```bash
# 1. Go to NuGet.org
# https://www.nuget.org/packages/SharpCoreDB/

# 2. Click the version number
# 3. Click "Delete" or "Unlist"

# 4. Document the issue
# Create a GitHub release note explaining why

# 5. Release a fixed version
# Increment patch version and republish
```

### Downtime / Emergency Maintenance

```bash
# If you need to temporarily block publishing:
gh secret set NUGET_API_KEY --body "TEMPORARILY_DISABLED"

# This will fail publish attempts (keeping code safe)
# Then restore the real key when ready:
gh secret set NUGET_API_KEY --body "your-real-api-key"
```

---

## 📊 Metrics to Track

### Performance Metrics

```bash
# Build time (should be <20 minutes)
# - Ubuntu: usually fastest
# - Windows: might be slower
# - macOS: usually slowest

# Test execution time
# - SharpCoreDB.Tests: should be <10 minutes
# - VectorSearch.Tests: should be <5 minutes

# Publishing time
# - Pack: <2 minutes
# - Publish: <1 minute
```

### Quality Metrics

```bash
# Code coverage: Target 18%+ (configured)
# Deprecated packages: Target 0
# Vulnerable packages: Target 0
# Build warnings: Target 0
# Test failures: Target 0
```

### Adoption Metrics

```bash
# Track on NuGet.org:
# - Total downloads
# - Downloads per version
# - Adoption rate (% using latest)
# - User ratings and reviews

# Command to check:
# Visit: https://www.nuget.org/packages/SharpCoreDB/
```

---

## 🔐 Security Checklist

### Monthly
- [ ] Verify `NUGET_API_KEY` secret exists
- [ ] Check no secrets leaked in recent commits
- [ ] Review GitHub Actions logs for errors
- [ ] Verify package authenticity on NuGet.org

### Quarterly
- [ ] Check API key expiration date
- [ ] Review access control settings
- [ ] Audit who has push access to master
- [ ] Test API key rotation procedure

### Annually
- [ ] Rotate `NUGET_API_KEY`
- [ ] Review all GitHub secrets
- [ ] Update security policies
- [ ] Conduct security audit of CI/CD

---

## 🎓 Common Mistakes to Avoid

### ❌ Mistake 1: Pushing Without Testing
```bash
# WRONG
git add .
git commit -m "Changes"
git push  # Oops, tests fail in CI!

# RIGHT
dotnet test -c Release  # Test first
git push  # Only after passing locally
```

### ❌ Mistake 2: Forgetting to Update Versions
```bash
# WRONG
# Edit code, push, but forgot to update <Version>
# Result: Same version published twice (wasted action)

# RIGHT
# Check versions before pushing
grep "<Version>" src/**/*.csproj
# All should show same version (or intentionally different)
```

### ❌ Mistake 3: Committing Secrets
```bash
# WRONG
git add nuget.config
git commit -m "Add API key"
git push  # SECURITY BREACH!

# RIGHT
# Use GitHub Secrets instead
# Never commit credentials
echo "*.key" >> .gitignore
```

### ❌ Mistake 4: Ignoring Failed Tests
```bash
# WRONG
# Tests fail locally
# Push anyway, hoping CI fixes it
# Result: Publishing is blocked by build failures

# RIGHT
# Fix locally first
dotnet test -c Release
# Get all tests passing before push
git push
```

### ❌ Mistake 5: Manual Publishing Every Time
```bash
# WRONG
# Remember to manually run "dotnet pack"
# Remember to manually run "dotnet nuget push"
# Easy to forget, easy to make mistakes

# RIGHT
# Let CI/CD handle it automatically
# Just commit and push
# Relax while automation does the work ✨
```

---

## ✅ Success Indicators

Your CI/CD is working well if:

- ✅ Every push to master automatically publishes (after tests pass)
- ✅ New packages appear on NuGet.org within 2 minutes
- ✅ No manual `dotnet pack` or `dotnet nuget push` commands needed
- ✅ GitHub Actions page shows green checkmarks consistently
- ✅ Team members can deploy without CI/CD knowledge
- ✅ Hotfixes go from commit to production in <15 minutes
- ✅ Zero secrets in source code
- ✅ Complete audit trail in GitHub Actions logs

---

## 📚 Quick Reference Links

| Resource | URL |
|----------|-----|
| NuGet Publishing | https://docs.microsoft.com/nuget/nuget-org/publish-a-package |
| dotnet CLI | https://learn.microsoft.com/en-us/dotnet/core/tools/ |
| GitHub Actions | https://docs.github.com/actions |
| Semantic Versioning | https://semver.org/ |
| Your Package | https://www.nuget.org/packages/SharpCoreDB/ |
| Your Repository | https://github.com/MPCoreDeveloper/SharpCoreDB |

---

## 💡 Pro Tips

1. **Use `git log --oneline -10`** to verify commit history before pushing
2. **Watch GitHub Actions in real-time** by keeping the Actions tab open
3. **Star your package on NuGet** to make it easier to find
4. **Create milestone releases** with detailed release notes
5. **Monitor NuGet trends** to understand version adoption
6. **Set up GitHub notifications** to get alerts on failed builds
7. **Use GitHub CLI** (`gh`) for faster automation workflows
8. **Document your release process** for team knowledge sharing

---

**Created**: 2025-01-28  
**Last Updated**: 2025-01-28  
**Status**: Production Ready
