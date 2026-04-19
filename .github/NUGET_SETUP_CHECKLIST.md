# NuGet Publishing Setup Checklist

## ✅ Pre-Setup Verification

- [ ] You have a GitHub account with admin access to the repository
- [ ] You have a NuGet.org account
- [ ] Your projects are versioned (check `.csproj` files for `<Version>` tags)

---

## 🔑 Step 1: Create NuGet API Key (5 minutes)

1. [ ] Log in to https://www.nuget.org
2. [ ] Go to **Account → API Keys** (https://www.nuget.org/account/apikeys)
3. [ ] Click **Create** (or **Create New Key**)
4. [ ] Fill in the form:
   - **Key Name**: `GitHub Actions - SharpCoreDB` (or descriptive name)
   - **Select Scopes**: ✓ Push new packages and package versions
   - **Select Package**: All (or specific packages if preferred)
   - **Expiration**: Set a date (e.g., 1 year from now)
5. [ ] Click **Create**
6. [ ] **Copy the API key immediately** (you won't see it again!)
7. [ ] Keep the key safe (you'll need it next)

---

## 🔐 Step 2: Add GitHub Secret (3 minutes)

1. [ ] Go to your GitHub repository: https://github.com/MPCoreDeveloper/SharpCoreDB
2. [ ] Click **Settings** (top menu)
3. [ ] Click **Secrets and variables** (left sidebar)
4. [ ] Click **Actions**
5. [ ] Click **New repository secret** (green button)
6. [ ] Fill in:
   - **Name**: `NUGET_API_KEY` (exactly this case)
   - **Value**: Paste the API key from Step 1
7. [ ] Click **Add secret**
8. [ ] Verify it appears in the list (masked with dots) ✓

---

## 📋 Step 3: Verify Project Setup (2 minutes)

Check that your projects have version information:

1. [ ] Open `src/SharpCoreDB/SharpCoreDB.csproj`
2. [ ] Find the `<Version>` tag:
   ```xml
   <Version>1.7.0</Version>
   ```
3. [ ] Repeat for other `src/` projects that should be published:
   - [ ] `src/SharpCoreDB.Data.Provider/SharpCoreDB.Data.Provider.csproj`
   - [ ] `src/SharpCoreDB.Extensions/SharpCoreDB.Extensions.csproj`
   - [ ] `src/SharpCoreDB.EventSourcing/SharpCoreDB.EventSourcing.csproj`
   - [ ] `src/SharpCoreDB.Analytics/SharpCoreDB.Analytics.csproj`
   - [ ] `src/SharpCoreDB.VectorSearch/SharpCoreDB.VectorSearch.csproj`
   - [ ] Other publishable projects...

**Note**: Test projects and example projects don't need versions.

---

## 🚀 Step 4: Test the Automation (5 minutes)

1. [ ] Make a small change to the repository
   ```bash
   echo "# Testing NuGet automation" >> TEST_AUTOMATION.md
   git add .
   git commit -m "Test: Verify NuGet publishing automation"
   git push origin master
   ```

2. [ ] Go to **GitHub → Actions** tab
3. [ ] Watch the workflow:
   - [ ] ✅ `build` job completes successfully
   - [ ] ✅ `pack` job completes successfully
   - [ ] ✅ `publish` job completes successfully
4. [ ] Check the publish job logs for:
   ```
   Pushing SharpCoreDB.x.x.x.nupkg to 'https://api.nuget.org/v3/index.json'...
   OK https://api.nuget.org/v3/index.json
   ```

5. [ ] Optional: Verify on NuGet.org
   - [ ] Visit https://www.nuget.org/packages/SharpCoreDB/
   - [ ] Check **Version History** for the new version

---

## 🎯 Step 5: Document the Process (5 minutes)

1. [ ] Review the generated documentation:
   - [ ] `.github/NUGET_PUBLISHING_GUIDE.md` - Full guide
   - [ ] `.github/NUGET_QUICK_REFERENCE.md` - Quick reference
   - [ ] `.github/workflows/ci.yml` - Updated workflow
   - [ ] `.github/workflows/publish-manual.yml` - Manual publish option

2. [ ] Share these docs with your team:
   ```bash
   # Link them in your README or documentation
   - [NuGet Publishing Guide](.github/NUGET_PUBLISHING_GUIDE.md)
   - [Quick Reference](.github/NUGET_QUICK_REFERENCE.md)
   ```

3. [ ] Update team documentation with the publishing process

---

## 🎉 Step 6: Going Live (1 minute)

1. [ ] Remove the test file:
   ```bash
   git rm TEST_AUTOMATION.md
   git commit -m "Remove: Automation test file"
   git push origin master
   ```

2. [ ] From now on:
   - [ ] Every push to `master` that passes tests = automatic publish
   - [ ] Update version in `.csproj` files to release new versions
   - [ ] Optionally use `publish-manual.yml` for on-demand publishing

3. [ ] Monitor first few releases to ensure everything works

---

## 📚 Step 7: Configure Optional Features (as needed)

### Enable Tag-Based Publishing Only
If you prefer publishing only on git tags:

Edit `.github/workflows/ci.yml`:
```yaml
on:
  push:
    tags: ['v*']
```

### Enable Automatic Versioning
Use a tool like [MinVer](https://github.com/adamralph/minver) for semantic versioning from git tags (advanced).

### Setup Slack/Email Notifications
Add GitHub Actions notification rules to alert on publish success/failure.

---

## ✨ Success Criteria

After completing all steps, you should have:

- [ ] ✅ NuGet API key created
- [ ] ✅ `NUGET_API_KEY` secret configured in GitHub
- [ ] ✅ `.github/workflows/ci.yml` updated with publish job
- [ ] ✅ `.github/workflows/publish-manual.yml` created
- [ ] ✅ All projects have `<Version>` tags
- [ ] ✅ First test publish succeeded
- [ ] ✅ New packages visible on NuGet.org
- [ ] ✅ Documentation reviewed by team

---

## 🆘 Need Help?

| Issue | Solution |
|-------|----------|
| Can't find API keys page | Go to https://www.nuget.org/account/apikeys |
| Can't find Secrets settings | Go to: Repo → Settings → Secrets and variables → Actions |
| Publish job failing | Check GitHub Actions logs for error messages |
| Packages not on NuGet | Wait 1-2 minutes and refresh https://www.nuget.org/packages/SharpCoreDB/ |
| API key expired | Create new key, update `NUGET_API_KEY` secret |

---

## 🔄 Ongoing Maintenance

### Monthly
- [ ] Monitor NuGet package stats: https://www.nuget.org/packages/SharpCoreDB/
- [ ] Review GitHub Actions workflow runs

### Quarterly
- [ ] Check API key expiration date
- [ ] Review and update versions as needed

### Yearly
- [ ] Refresh/rotate NuGet API key
- [ ] Update security settings if needed

---

## 📞 Contact & Support

- GitHub Issues: https://github.com/MPCoreDeveloper/SharpCoreDB/issues
- NuGet Support: https://www.nuget.org/contact/abuse
- Microsoft Docs: https://learn.microsoft.com/nuget/

---

## 📝 Completion Tracking

| Date | Completion Status | Notes |
|------|-------------------|-------|
| 2025-01-28 | ⏳ Pending | Setup in progress |
| | | |

---

**Generated**: 2025-01-28  
**Last Updated**: 2025-01-28  
**Status**: Ready for implementation
