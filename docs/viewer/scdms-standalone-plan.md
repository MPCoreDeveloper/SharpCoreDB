# SCDMS — Sharp Core Database Management System
## Plan: standalone repo, web-installatie en update-mechanisme

**Status:** Vastgelegd & in uitvoering
**Datum:** 2026-08-02
**Vervolg op:** `web-viewer-razor-pages-plan.md`
**Kosten:** €0 (alleen gratis OSS-infrastructuren)

---

## 1. Vastgelegde beslissingen

| # | Beslissing | Uitkomst |
|---|---|---|
| 1 | Naam | **SCDMS** (Sharp Core Database Management System, naar analogie van SSMS). Volledige rebrand: repo `MPCoreDeveloper/SCDMS`, namespaces `Scdms.*`, binary `scdms`/`scdms.exe`, installatie- en gegevenspaden `SCDMS`. Beschikbaarheid geverifieerd op GitHub en NuGet (2026-08-02). |
| 2 | Git-historie | **Schone start** met de huidige staat van `tools/SharpCoreDB.WebViewer` (geen `git filter-repo`). |
| 3 | Versie | Start op **1.0.0.0** (assembly/file version), release-tag `v1.0.0` (SemVer). |
| 4 | Distributievorm | **Self-contained single-file** binaries per OS — geen .NET-installatie bij de gebruiker vereist. |
| 5 | Oude Avalonia `tools/SharpCoreDB.Viewer` | **Volledig laten staan** in de hoofdrepo; alleen documentatie bijwerken met verwijzing naar SCDMS. |

---

## 2. Doelen

- SCDMS als zelfstandig product in eigen repo: `github.com/MPCoreDeveloper/SCDMS`.
- Installatie vanaf het web met één commando op Windows, Linux en macOS.
- Update-mechanisme via in-app notificatie + self-update, aangevuld met gratis package managers.
- Secure-by-default behouden (SafeWebCore strict A+, HTTPS, geen wachtwoord-persistentie).

### Non-goals (v1.0.0)
- Geen betaalde infrastructuur (geen Apple-notarisatie, geen EV-certificaat).
- Geen Linux .deb/.rpm/Snap/Flatpak (uitgesteld; install-script + Homebrew-on-Linux dekken het doel).
- Geen cloud/multi-tenant hosting.
- One-click in-app self-update is v1.1-scope; v1.0.0 updatet via her-run van het install-script (`scdms --update`).

---

## 3. Fase 1 — Repo-splitsing

1. Nieuwe repo `MPCoreDeveloper/SCDMS`, schone start vanuit huidige staat.
2. Layout: `src/SCDMS/` (de app), `install/`, `.github/workflows/`, `docs/`, `SCDMS.slnx`, `Directory.Build.props` (1.0.0.0), `Directory.Packages.props`, `NuGet.Config` (alleen nuget.org), `LICENSE` (MIT), `.gitignore`, `README.md`.
3. **Kritiek:** `ProjectReference` → `PackageReference` naar NuGet.org: `SharpCoreDB`, `SharpCoreDB.Data.Provider`, `SharpCoreDB.Client`, plus `SafeWebCore` en `SharpDispatch` (centraal gepind).
4. Rebrand: namespaces `SharpCoreDB.WebViewer.*` → `Scdms.*`; `WebViewerOptions` → `ScdmsOptions`; config-sectie + env-prefix `WebViewer__` → `SCDMS__`; session-cookie → `.SCDMS.Session`; UI-titel "SCDMS — Sharp Core Database Management System"; versie in UI dynamisch uit assembly.
5. **Gebruikersdata-migratie (eenmalig):** `%LOCALAPPDATA%\SharpCoreDB.WebViewer\` → `%LOCALAPPDATA%\SCDMS\` (settings.json, query-workspace.json, Data\ met databases). Auto-migratie bij eerste start + notificatie.
6. Hoofdrepo: `tools/SharpCoreDB.WebViewer` blijft tijdens overgang bestaan; docs verwijzen naar de nieuwe repo.

## 4. Fase 2 — Standalone-readiness + release-engineering

### 4.1 HTTPS zonder .NET SDK (blokker, grootste werkitem)
- Bij eerste start **in-process** een self-signed `localhost`-certificaat genereren (`CertificateRequest`, RSA 2048, SAN: localhost, 127.0.0.1, ::1).
- Opslaan onder SCDMS-gebruikersmap (`certs\localhost.pfx` + random pfx-wachtwoord, user-only bestandsrechten).
- Koppelen via `ConfigureKestrel(...).Listen(..., o => o.UseHttps(cert))`.
- Browserwaarschuwing eenmalig accepteren; trust-instructies per OS in installer en docs.
- HTTP-fallback afgekeurd (breekt `Secure`-cookies en het A+-securitypostuur).

### 4.2 CI-workflow (`.github/workflows/ci.yml`)
- Build + smoke-test matrix: `ubuntu-latest`, `windows-latest`, `macos-latest`, .NET 10.
- Deprecated/vulnerable package checks (patroon uit hoofdrepo).
- Gratis: GitHub Actions is kosteloos voor public repos.

### 4.3 Release-workflow (`.github/workflows/release.yml`)

## 5. Fase 3 — Installatie vanaf het web (GitHub)

Scripts in `install/`, geserveerd via `raw.githubusercontent.com` (gratis):

**Windows (PowerShell, geen admin):**
```powershell
irm https://raw.githubusercontent.com/MPCoreDeveloper/SCDMS/main/install/install.ps1 | iex
```
→ `%LOCALAPPDATA%\Programs\SCDMS\`, Start Menu-snelkoppeling, optioneel user-PATH.

**Linux / macOS (bash):**
```bash
curl -fsSL https://raw.githubusercontent.com/MPCoreDeveloper/SCDMS/main/install/install.sh | bash
```
→ auto-detect OS/arch, `~/.local/share/scdms/`, symlink `~/.local/bin/scdms`, `.desktop`-entry (Linux), `.command`-launcher (macOS).

Script-eigenschappen:
- Downloadt latest release via GitHub API (of `-Version v1.0.0` pin).
- **SHA256-verificatie** verplicht.
- Idempotent: opnieuw draaien = updaten (dit is tevens het update-mechanisme).
- `--uninstall` flag.
- macOS Gatekeeper: geen issue bij `curl`-downloads (geen quarantine-xattr) — gedocumenteerd.
- Windows SmartScreen bij ongesigneerde binary: gedocumenteerd + checksums; later optioneel SignPath.io (gratis voor OSS).

## 6. Fase 4 — Update-mechanisme

### 6.1 In-app (v1.0.0)
- `IUpdateCheckService`: pollt `https://api.github.com/repos/MPCoreDeveloper/SCDMS/releases/latest` (max 1×/24u, gecachet; 60 req/u unauthenticated is ruim voldoende).
- SemVer-vergelijking met eigen `AssemblyInformationalVersion`.
- UI-banner: "SCDMS vX.Y.Z beschikbaar" met release-link.
- Update-actie v1.0.0: her-run install-script; CLI-equivalent `scdms --update` (start platform-installer). One-click vanuit de browser is v1.1-scope.

### 6.2 Package managers (gratis, na v1.0.0)
| Kanaal | Repo/actie | Update-commando |
|---|---|---|
| Scoop (Windows) | eigen bucket `MPCoreDeveloper/scoop-bucket`, manifest auto-update via release-workflow | `scoop update scdms` |
| Homebrew (macOS+Linux) | eigen tap `MPCoreDeveloper/homebrew-tap`, formule auto-update via release-workflow | `brew upgrade scdms` |
| WinGet (Windows) | manifest-PR naar `microsoft/winget-pkgs` via `wingetcreate` | `winget upgrade MPCoreDeveloper.SCDMS` |

## 7. Fase 5 — Documentatie & launch

- Nieuwe repo: README met badges + one-liners, uninstall-instructies, SECURITY.md.
- Hoofdrepo: `docs/viewer/` + WebViewer README verwijzen naar SCDMS-repo; Avalonia Viewer blijft ongemoeid (beslissing 5).
- Release-checklist + eerste release `v1.0.0`.

---

## 8. Risico's en mitigaties

| Risico | Mitigatie |
|---|---|
| HTTPS-certificaat zonder .NET SDK | Fase 4.1: in-process self-signed cert + per-OS trust-docs |
| NuGet-drift (app heeft ongerelease core-features nodig) | Versies pinnen; Dependabot in nieuwe repo; compatibiliteits-smoke-test in CI |
| SmartScreen/Gatekeeper-waarschuwingen | Documentatie + SHA256-checksums; later optioneel SignPath.io (gratis OSS) |
| GitHub API rate limits update-check | 1×/24u + cache; 60 req/u is ruim voldoende |
| Razor single-file publish edge cases | Smoke-test per RID in release-workflow vóór upload |

## 9. Acceptatiecriteria

- [x] `dotnet build` slaagt in nieuwe repo zonder verwijzing naar de hoofdrepo. *(geverifieerd 2026-08-03: 0 warnings, 0 errors)*
- [x] App start standalone (geen SDK) en serveert HTTPS op `https://localhost:5443`. *(geverifieerd: single-file `scdms.exe` 49 MB, `--version`/`--check-update` OK, HTTP 200, in-process self-signed cert)*
- [ ] Eén-commando-install werkt op Windows, Linux en macOS vanaf GitHub. *(scripts klaar; te verifiëren na eerste GitHub-release)*
- [x] Update-check toont banner bij nieuwere release; update her-gebruikt het install-script. *(endpoint `/api/update-check` geverifieerd; banner actief; repo moet nog online)*
- [x] Bestaande gebruikersdata wordt eenmalig gemigreerd zonder verlies. *(geverifieerd op dev-machine: settings, workspace en databases verhuisd naar `%LOCALAPPDATA%\SCDMS`)*
- [ ] Release `v1.0.0` publiceert 5 RID-artifacten + SHA256SUMS naar GitHub Releases. *(workflow klaar; wacht op repo-aanmaak + tag)*
- [x] Kosten: €0.

### Uitvoeringsstatus (2026-08-03)

- ✅ **Fase 1** gereed: lokale repo `D:\repos\MPCoreDeveloper\SCDMS` (branch `main`, 2 commits), volledige rebrand, NuGet-conversie (SharpCoreDB 1.9.3 van NuGet.org).
- ✅ **Fase 2.1** gereed: in-process self-signed localhost-certificaat (`LocalhostCertificateProvider`).
- ✅ **Fase 2.2/2.3** klaar: `ci.yml` (matrix + smoke) en `release.yml` (5 RID's, SHA256, GitHub Release).
- ✅ **Fase 3** klaar: `install/install.ps1` + `install/install.sh` (idempotent, checksum-verificatie, `--uninstall`).
- ✅ **Fase 4.1** gereed: `IUpdateCheckService` (24u-cache), UI-banner, `scdms --update` / `--check-update` / `--version`.
- ⏳ **Restant (vereist GitHub):** repo aanmaken `MPCoreDeveloper/SCDMS`, pushen, tag `v1.0.0` zetten, eerste release verifiëren, daarna Fase 4.2 (Scoop/Homebrew/WinGet) en Fase 5 afronden.

- Trigger: tag `v*`.
- `dotnet publish` self-contained + `PublishSingleFile` voor: `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`.
- `.zip` (Windows) / `.tar.gz` (Unix) + `SHA256SUMS.txt`.
- GitHub Release met auto-release-notes; versie uit tag → `/p:Version=` + `AssemblyInformationalVersion`.
