# ?? SharpCoreDB Benchmarks - SIMPEL!

## HOE TE GEBRUIKEN (2 STAPPEN!)

### Stap 1: Ga naar de benchmark folder
```powershell
cd D:\source\repos\MPCoreDeveloper\SharpCoreDB\SharpCoreDB.Benchmarks
```

### Stap 2: Run het menu script
```powershell
.\RunComprehensive.ps1
```

### Stap 3: Kies wat je wilt testen
```
Select benchmark:
  1. Quick 10K Test (RECOMMENDED - Fast!)     ? KIES DIT!
  2. Full INSERT Benchmarks
  3. Full SELECT Benchmarks
  4. Full UPDATE/DELETE Benchmarks
  5. Run ALL Benchmarks
  Q. Quit

Enter choice (1-5 or Q): 1
```

## DAT IS HET! ??

Geen ingewikkelde scripts meer, geen aparte projecten, gewoon:
1. Run `.\RunComprehensive.ps1`
2. Kies optie 1
3. Wacht 2-3 minuten
4. Zie de resultaten!

---

## Wat Test Optie 1?

**Quick 10K Test** test 10,000 record inserts met:
- ? SharpCoreDB (No Encryption)
- ? SharpCoreDB (Encrypted)  
- ? SQLite (Memory)
- ? SQLite (File + WAL + FullSync)
- ? LiteDB

**Output**:
```
BenchmarkDotNet v0.14.0, Windows 11
Intel Core i7-10850H CPU 2.70GHz

| Method                            | Mean     | Ratio |
|-----------------------------------|----------|-------|
| SQLite (Memory)                   | 73.0 ms  | 1.00  |
| SQLite (File + WAL + FullSync)    | 46.0 ms  | 0.63  |
| LiteDB                            | 418 ms   | 5.73  |
| SharpCoreDB (No Encryption)       | 7,695 ms | 105.4 |
| SharpCoreDB (Encrypted)           | 42,903ms | 587.7 |
```

---

## Andere Opties

- **Optie 2**: Test INSERT met 1, 10, 100, 1000, 10000 records
- **Optie 3**: Test SELECT (point queries, range, scans)
- **Optie 4**: Test UPDATE en DELETE
- **Optie 5**: Run ALLES (duurt 20-30 minuten!)

---

## Resultaten Vinden

Na het runnen:
```
?? Results saved to: BenchmarkDotNet.Artifacts\results\

Formats:
  • HTML reports (open in browser)
  • CSV files (open in Excel)
  • JSON data
  • Markdown tables
```

---

## Troubleshooting

**Fout: "Cannot find path"**
- Zorg dat je in `SharpCoreDB.Benchmarks` directory bent
- Run: `cd D:\source\repos\MPCoreDeveloper\SharpCoreDB\SharpCoreDB.Benchmarks`

**Script werkt niet**
- Zorg dat PowerShell execution policy goed staat:
  ```powershell
  Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned
  ```

**Build errors**
- Run eerst: `dotnet build -c Release`

---

## SAMENVATTING

**WAT ER IS VERANDERD**:
- ? GEEN aparte Quick10kBenchmark project meer
- ? GEEN ingewikkelde scripts meer
- ? ALLES zit nu in het normale BenchmarkDotNet menu
- ? 1 simpel script: `RunComprehensive.ps1`
- ? Kies optie 1 voor snelle test!

**ZO SIMPEL IS HET NU**! ??
