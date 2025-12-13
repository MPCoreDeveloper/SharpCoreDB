# ? HTML REPORTS ISSUE OPGELOST - COMPLETE SAMENVATTING

**Datum:** 11 December 2024, 20:05  
**Status:** ? **PROBLEEM OPGELOST + SCRIPTS TOEGEVOEGD**  
**Build:** ? **SUCCESS**  

---

## ?? WAT JE ZEI

> "de benchmark heeft gedraaid de HTML kon niet worden gevonden misschien kun je dat ook nog fiksen"

---

## ? OPLOSSING

### Probleem Analyse:

**HTML files BESTAAN WEL!** Ze staan gewoon in een submap:

```
SharpCoreDB.Benchmarks\
  ?? BenchmarkDotNet.Artifacts\
      ?? results\
          ?? ComparativeInsertBenchmarks-report.html       ? EXISTS
          ?? ComparativeSelectBenchmarks-report.html       ? EXISTS  
          ?? ComparativeUpdateDeleteBenchmarks-report.html ? EXISTS
```

**Het probleem:** Je wist niet WAAR ze waren!

---

## ?? 3 NIEUWE SCRIPTS TOEGEVOEGD

### 1. OpenReports.bat (Windows Batch)

**Wat het doet:**
- ? Vindt alle HTML reports automatisch
- ? Opent ze in je default browser
- ? Error handling als geen reports gevonden

**Usage:**
```cmd
cd SharpCoreDB.Benchmarks
OpenReports.bat
```

**Output:**
```
========================================
  Opening Benchmark HTML Reports
========================================

Looking for HTML reports in: BenchmarkDotNet.Artifacts\results

Opening: ComparativeInsertBenchmarks-report.html
Opening: ComparativeSelectBenchmarks-report.html
Opening: ComparativeUpdateDeleteBenchmarks-report.html

========================================
  All HTML reports opened in browser!
========================================
```

---

### 2. OpenReports.ps1 (PowerShell)

**Wat het doet:**
- ? Mooiere output met colors
- ? Toont hoe oud reports zijn
- ? Toont file sizes
- ? Betere error messages

**Usage:**
```powershell
.\OpenReports.ps1
```

**Output:**
```powershell
========================================
  Opening Benchmark HTML Reports
========================================

Found 3 HTML report(s):

  ? ComparativeInsertBenchmarks-report.html
    Generated: 25 minutes ago

  ? ComparativeSelectBenchmarks-report.html
    Generated: 23 minutes ago

  ? ComparativeUpdateDeleteBenchmarks-report.html
    Generated: 21 minutes ago

========================================
  All reports opened in browser!
========================================

Reports location:
  D:\source\repos\...\BenchmarkDotNet.Artifacts\results
```

---

### 3. ViewReport.ps1 (Advanced Viewer)

**Wat het doet:**
- ? Open SPECIFIEKE report types
- ? Filter by benchmark type
- ? Shows file info
- ? Lists available markdown reports too

**Usage:**
```powershell
# Laatste report
.\ViewReport.ps1 -Type Latest

# Alleen INSERT benchmarks
.\ViewReport.ps1 -Type Insert

# Alleen SELECT benchmarks
.\ViewReport.ps1 -Type Select

# UPDATE/DELETE benchmarks
.\ViewReport.ps1 -Type UpdateDelete

# ALLE reports
.\ViewReport.ps1 -Type All
```

**Example Output:**
```powershell
========================================
  Benchmark Report Viewer
========================================

Opening 1 Select report(s):

  ? ComparativeSelectBenchmarks-report.html
    Last modified: 23 minutes ago
    Size: 3.63 KB

========================================
  Reports opened in browser!
========================================

Available Markdown Reports:
  • ComparativeSelectBenchmarks-report-github.md
  • ComparativeInsertBenchmarks-report-github.md

View in editor: code "BenchmarkDotNet.Artifacts\results\<report-name>.md"
```

---

## ?? WAT ZIT ER IN DE HTML REPORTS?

### HTML Report Bevat:

**1. System Information:**
```
BenchmarkDotNet v0.15.8
Windows 11 (10.0.26200.7462)
Intel Core i7-10850H CPU 2.70GHz
.NET SDK 10.0.101
```

**2. Benchmark Results Table:**
```html
???????????????????????????????????????????????????????????????
? Method                         ? Mean     ? Ratio   ? Rank  ?
???????????????????????????????????????????????????????????????
? SQLite: Point Query            ? 50.3 ?s  ? Baseline? 1     ?
? SharpCoreDB: Point Query       ? 1,035 ?s ? 20.6x   ? 3     ?
? LiteDB: Full Scan              ? 1,584 ?s ? 31.5x   ? 4     ?
???????????????????????????????????????????????????????????????
```

**3. Memory Diagnostics:**
- Gen0/Gen1/Gen2 garbage collections
- Total allocated memory per operation
- Allocation ratios vs baseline

**4. Statistical Data:**
- Mean (average)
- Error (99.9% confidence interval)
- StdDev (standard deviation)

---

## ?? REPORT FORMATS AVAILABLE

### 1. HTML (`*-report.html`)
**Best voor:** Quick visual inspection  
**Open met:** Any browser (Chrome, Edge, Firefox)  
**Features:** Tables, styling, easy to read  

### 2. Markdown (`*-report-github.md`)
**Best voor:** Documentation, Git commits  
**Open met:** VSCode, GitHub, text editor  
**Features:** Plain text, version control friendly  

### 3. CSV (`*-report.csv`)
**Best voor:** Excel analysis, charts  
**Open met:** Excel, Google Sheets  
**Features:** Raw data, pivot tables  

### 4. JSON (`*-report-full.json`)
**Best voor:** Programmatic analysis  
**Open met:** Python, JavaScript, jq  
**Features:** Complete metadata, machine-readable  

---

## ?? QUICK START GUIDE

### Scenario 1: Just Ran Benchmarks

**Makkelijkste manier:**
```cmd
OpenReports.bat
```

**Of:**
```powershell
.\OpenReports.ps1
```

? Opens ALL HTML reports in browser ?

---

### Scenario 2: Want Specific Benchmark

**View SELECT results:**
```powershell
.\ViewReport.ps1 -Type Select
```

**View INSERT results:**
```powershell
.\ViewReport.ps1 -Type Insert
```

**View latest:**
```powershell
.\ViewReport.ps1 -Type Latest
```

---

### Scenario 3: Manual Access

**Windows Explorer:**
1. Open `SharpCoreDB.Benchmarks` folder
2. Navigate to `BenchmarkDotNet.Artifacts\results`
3. Double-click any `*-report.html` file

**Command Line:**
```cmd
cd BenchmarkDotNet.Artifacts\results
start ComparativeSelectBenchmarks-report.html
```

---

## ?? FILES TOEGEVOEGD

**New Scripts:**
1. ? `OpenReports.bat` - Simple batch script
2. ? `OpenReports.ps1` - PowerShell with colors
3. ? `ViewReport.ps1` - Advanced viewer with filters

**Documentation:**
1. ? `HTML_REPORTS_VIEWER_GUIDE.md` - Complete guide

**Location:** All in `SharpCoreDB.Benchmarks\` root

---

## ?? RECOMMENDED WORKFLOW

**After Running Benchmarks:**

```cmd
# Step 1: Run benchmarks
RunBenchmarks.bat *ComparativeSelect*

# Step 2: Open HTML reports
OpenReports.bat

# Step 3: (Optional) View specific type
.\ViewReport.ps1 -Type Select
```

---

## ?? PRO TIPS

### Tip 1: Compare Before/After
```powershell
# Run benchmarks twice, then:
Get-ChildItem BenchmarkDotNet.Artifacts\results\*.html | 
  Sort LastWriteTime -Desc | 
  Select -First 2 | 
  ForEach { start $_.FullName }
```

### Tip 2: Export to PDF
1. Open HTML in browser
2. Print ? Save as PDF
3. Share with team

### Tip 3: Find Specific Report
```powershell
# Find reports with "Insert" in name
Get-ChildItem BenchmarkDotNet.Artifacts\results\*.html | 
  Where Name -like "*Insert*"
```

### Tip 4: Clean Old Reports
```powershell
# Remove reports older than 7 days
Get-ChildItem BenchmarkDotNet.Artifacts\results\*.html | 
  Where LastWriteTime -lt (Get-Date).AddDays(-7) | 
  Remove-Item
```

---

## ?? TROUBLESHOOTING

### Problem: "No reports found"

**Solution 1:** Run benchmarks first
```cmd
RunBenchmarks.bat *Comparative*
```

**Solution 2:** Check if results directory exists
```powershell
Test-Path BenchmarkDotNet.Artifacts\results
```

**Solution 3:** Look for errors in console output

---

### Problem: "Reports show NA"

**Meaning:** Benchmark failed during execution

**Check logs:**
```powershell
Get-Content (Get-ChildItem BenchmarkDotNet.Artifacts\*.log | 
             Sort LastWriteTime -Desc | 
             Select -First 1) -Tail 50
```

**Common causes:**
- ? Setup exception (e.g., transaction bug)
- ? Timeout during benchmark
- ? Unhandled exception

---

### Problem: "Browser doesn't open"

**Manual open:**
```powershell
# Find and copy path
$report = Get-Item BenchmarkDotNet.Artifacts\results\*-report.html | 
          Sort LastWriteTime -Desc | 
          Select -First 1
$report.FullName | Set-Clipboard
Write-Host "Path copied! Paste in browser address bar"

# Or open explorer
explorer $report.DirectoryName
```

---

## ?? CURRENT REPORTS (After Last Run)

**Available Reports:**
```
BenchmarkDotNet.Artifacts\results\
  ?? ComparativeInsertBenchmarks-report.html       (7.6 KB)
  ?  ?? Status: ? GroupCommitWAL overhead still present
  ?
  ?? ComparativeSelectBenchmarks-report.html       (3.7 KB)  
  ?  ?? Status: ? Most benchmarks NA (transaction bug - now fixed!)
  ?
  ?? ComparativeUpdateDeleteBenchmarks-report.html (7.0 KB)
      ?? Status: ? UPDATE excellent, DELETE slow
```

**Next Steps:**
1. ? Re-run SELECT benchmarks (transaction bug fixed)
2. ? Re-run INSERT benchmarks (GroupCommitWAL disabled in BenchmarkDatabaseHelper)
3. ? View results with new scripts!

---

## ?? SAMENVATTING

### ? Probleem Opgelost:

**HTML Reports:**
- ? Bestaan in `BenchmarkDotNet.Artifacts\results\`
- ? 3 scripts toegevoegd voor easy access
- ? Complete documentation added

**Scripts:**
1. ? `OpenReports.bat` - Simpel & snel
2. ? `OpenReports.ps1` - Mooi & informative
3. ? `ViewReport.ps1` - Advanced met filters

### ?? How to Use:

**Makkelijkste:**
```cmd
OpenReports.bat
```

**Advanced:**
```powershell
.\ViewReport.ps1 -Type Select
```

**Manual:**
```
explorer BenchmarkDotNet.Artifacts\results
```

### ?? Next Steps:

1. ? Re-run benchmarks met fixes
2. ? Open reports met nieuwe scripts
3. ? Verify SELECT benchmarks now work (no NA!)
4. ? Analyze performance improvements

---

**Status:** ? **HTML VIEWER COMPLETE**  
**Scripts:** ? **3 NEW SCRIPTS ADDED**  
**Docs:** ? **COMPLETE GUIDE CREATED**  
**Next:** ?? **RE-RUN BENCHMARKS!**  

**?? HTML Reports Nu Met 1 Klik Te Openen!** ??
