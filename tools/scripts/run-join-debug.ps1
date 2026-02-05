$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Write-Host "Running JOIN validation with debug output..." -ForegroundColor Cyan
dotnet run --project (Join-Path $repoRoot "tests\SharpCoreDB.DemoJoinsSubQ") 2>&1 | Select-String -Pattern "(JOIN-DEBUG|WHERE-DEBUG|Validating|Returned|FAIL|All rows:)" -Context 0,2
