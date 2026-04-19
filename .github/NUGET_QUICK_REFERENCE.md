# NuGet Publishing Quick Reference

## 🚀 Quick Start

### 1. One-Time Setup (5 minutes)

```bash
# 1. Get your NuGet API key from https://www.nuget.org/account/apikeys
# 2. Add it as GitHub secret: NUGET_API_KEY
# 3. Done! ✅
```

### 2. Automatic Publishing

**Happens automatically on every push to `master` that passes tests:**

```bash
git add .
git commit -m "Add new feature"
git push origin master
# 📦 Packages automatically published to NuGet.org in ~5 minutes
```

### 3. Manual Publishing (Optional)

**If you need to publish on-demand:**

Go to: **GitHub Actions → Manual NuGet Publish → Run workflow**

Or use CLI:
```bash
gh workflow run publish-manual.yml -f reason="Hotfix for issue #123"
```

---

## 📋 Publishing Flow

```
┌─────────────────────────────────────────────────────────────┐
│ Push to master (must pass all tests)                        │
└──────────────────┬──────────────────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────────────────┐
│ ✅ build job (Windows, Linux, macOS)                        │
│    - Compile code                                            │
│    - Run unit tests                                          │
│    - Validate coverage (18% threshold)                       │
└──────────────────┬──────────────────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────────────────┐
│ 📦 pack job (after build succeeds)                          │
│    - Create .nupkg files                                    │
│    - Store in artifacts                                     │
└──────────────────┬──────────────────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────────────────┐
│ 🌐 publish job (after pack succeeds)                        │
│    - Download .nupkg files                                  │
│    - Push to NuGet.org                                      │
│    - Skip duplicates automatically                          │
└─────────────────────────────────────────────────────────────┘
```

---

## 🔐 Security Notes

- **API Key**: Stored as GitHub Secret (never visible in logs)
- **Scope**: NuGet API key should allow only "Push packages"
- **Expiration**: Set API key to expire periodically (recommended: yearly)
- **Rotation**: Update `NUGET_API_KEY` secret when key expires

---

## 📊 Version Management

### Current Setup
Each `.csproj` file contains:
```xml
<Version>1.7.0</Version>
```

### Publishing New Versions

**To release version 1.8.0:**

1. Update all `.csproj` files:
   ```xml
   <Version>1.8.0</Version>
   ```

2. Create a GitHub release (optional but recommended):
   ```bash
   git tag v1.8.0
   git push origin v1.8.0
   ```

3. Push changes to master:
   ```bash
   git add .
   git commit -m "Release v1.8.0"
   git push origin master
   ```

4. ✅ Packages published automatically

### Pre-release Versions

For beta/RC versions, use:
```xml
<Version>1.8.0-beta.1</Version>
<Version>1.8.0-rc.1</Version>
```

---

## ✅ Verify Publishing

### 1. Check GitHub Actions
- Go to **Actions** tab
- Click latest workflow run
- Verify **publish** job succeeded ✅

### 2. Check NuGet.org
- Visit: https://www.nuget.org/packages/SharpCoreDB/
- New version should appear within 1-2 minutes
- Check **Version History** tab

### 3. Try Installing
```bash
dotnet add package SharpCoreDB --version 1.8.0
```

---

## 🚨 Troubleshooting

| Problem | Solution |
|---------|----------|
| 401 Unauthorized | Verify `NUGET_API_KEY` secret is set correctly |
| Package already exists | Normal - duplicate versions are skipped. Update version to publish new release |
| Publish job not running | Ensure push is to `master` and build job passed tests |
| Slow publishing | NuGet.org indexing takes 1-2 minutes after push |

---

## 📚 Files Created/Modified

| File | Purpose |
|------|---------|
| `.github/workflows/ci.yml` | Updated with `publish` job |
| `.github/workflows/publish-manual.yml` | Manual publish workflow (optional) |
| `.github/NUGET_PUBLISHING_GUIDE.md` | Detailed guide |
| `README.md` (this file) | Quick reference |

---

## 🔗 Useful Links

- [NuGet Publishing Docs](https://docs.microsoft.com/nuget/nuget-org/publish-a-package)
- [Your NuGet API Keys](https://www.nuget.org/account/apikeys)
- [SharpCoreDB on NuGet](https://www.nuget.org/packages/SharpCoreDB/)
- [GitHub Actions Documentation](https://docs.github.com/actions)

---

## 💡 Tips

1. **Always test locally before pushing**:
   ```bash
   dotnet pack src/SharpCoreDB/SharpCoreDB.csproj --configuration Release
   ```

2. **Use semantic versioning**: Major.Minor.Patch (e.g., 1.7.0)

3. **Write release notes** in GitHub Releases for visibility

4. **Monitor NuGet stats** at https://www.nuget.org/packages/SharpCoreDB/

5. **Set API key expiration** to remind you to refresh annually

---

**Last Updated**: 2025-01-28
