# Quick fix for ComprehensiveComparison.cs duplicate key issue

$file = "D:\source\repos\MPCoreDeveloper\SharpCoreDB\SharpCoreDB.Benchmarks\ComprehensiveComparison.cs"
$content = Get-Content $file -Raw

# Fix line 646: Remove random ID generation
$content = $content -replace 'var id = threadId \* recordsPerThread \+ i \+ Random\.Shared\.Next\(1000000, 9000000\);', 'var id = threadId * recordsPerThread + i;'

Set-Content $file $content -NoNewline

Write-Host "Fixed! Replaced random ID with deterministic ID." -ForegroundColor Green
