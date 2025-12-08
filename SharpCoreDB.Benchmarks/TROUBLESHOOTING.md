# Benchmark Troubleshooting Guide

## Issue: "Total Reports: 0" - Benchmarks niet uitgevoerd

### Symptoom
```
Statistics:
  Total Benchmarks: 3
  Total Reports: 0
```

### Oorzaak
De benchmarks valideren maar voeren niet uit. Dit gebeurt wanneer:
1. BenchmarkDotNet detecteert validation errors
2. De benchmarks stoppen na validation fase

### Oplossing

#### Optie 1: Run in Debug mode eerst (aanbevolen)
```bash
cd SharpCoreDB.Benchmarks
dotnet build -c Debug
dotnet run -c Debug
```

Dit geeft meer output over waarom benchmarks niet draaien.

#### Optie 2: Simplify Configuration

Verwijder complexe diagnostics en reduceer test sizes voor sneller testen.

#### Optie 3: Run één benchmark tegelijk

```bash
# Test alleen Insert benchmarks
dotnet run -c Release -- --filter Insert

# Test alleen met 1 record
# Edit ComparativeInsertBenchmarks.cs: [Params(1)]
```

### ThreadingDiagnoser Warning

De warning:
```
* ThreadingDiagnoser supports only .NET Core 3.0+
```

Is niet kritiek - dit is alleen een waarschuwing. De benchmarks zouden moeten draaien.

### Mogelijke Oorzaken van "Total Reports: 0"

1. **Setup failures** - GlobalSetup faalt en benchmarks worden geskipped
2. **Database initialization fails** - BenchmarkDatabaseHelper constructor faalt
3. **Temp directory issues** - Kan geen temp directories maken

### Debug Steps

1. **Check Console Output**
   Kijk voor errors TIJDENS de benchmark runs, niet alleen warnings

2. **Run with verbosity**
   ```bash
   dotnet run -c Release -- --verbosity diagnostic
   ```

3. **Check BenchmarkDotNet.Artifacts**
   ```bash
   ls BenchmarkDotNet.Artifacts/results/
   cat BenchmarkDotNet.Artifacts/logs/*.log
   ```

4. **Test Database Helper**
   ```csharp
   // In Program.cs Main, add before benchmarks:
   Console.WriteLine("Testing database helper...");
   var testPath = Path.Combine(Path.GetTempPath(), "test");
   Directory.CreateDirectory(testPath);
   using (var helper = new BenchmarkDatabaseHelper(testPath))
   {
       helper.CreateUsersTable();
       helper.InsertUser(1, "Test", "test@test.com", 30, DateTime.Now, true);
       Console.WriteLine("? Database helper works!");
   }
   Directory.Delete(testPath, true);
   ```

### Immediate Fix

Ik ga nu een eenvoudigere benchmark maken die WEL werkt.
