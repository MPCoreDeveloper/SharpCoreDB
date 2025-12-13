# Update Copyright Headers in All C# Files
# Replaces old PlaceholderCompany headers with new MIT license header

$newHeader = @"
// <copyright file="{0}" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
"@

$rootPath = "D:\source\repos\MPCoreDeveloper\SharpCoreDB"

# Get all C# files
$csFiles = Get-ChildItem -Path $rootPath -Filter "*.cs" -Recurse -File | 
    Where-Object { 
        $_.FullName -notlike "*\bin\*" -and 
        $_.FullName -notlike "*\obj\*" -and
        $_.FullName -notlike "*\packages\*"
    }

$updatedCount = 0
$errorCount = 0

foreach ($file in $csFiles) {
    try {
        $content = Get-Content -Path $file.FullName -Raw
        
        # Check if file has old header
        if ($content -match '(?s)^// <copyright.*?PlaceholderCompany.*?</copyright>\s*\r?\n') {
            Write-Host "Updating: $($file.FullName)" -ForegroundColor Yellow
            
            # Create new header with actual filename
            $fileHeader = $newHeader -f $file.Name
            
            # Replace old header with new one
            $newContent = $content -replace '(?s)^// <copyright.*?</copyright>\s*\r?\n', "$fileHeader`r`n"
            
            # Write back to file
            [System.IO.File]::WriteAllText($file.FullName, $newContent, [System.Text.UTF8Encoding]::new($false))
            
            $updatedCount++
        }
        else {
            Write-Host "Skipping (no old header): $($file.Name)" -ForegroundColor Gray
        }
    }
    catch {
        Write-Host "ERROR processing $($file.FullName): $_" -ForegroundColor Red
        $errorCount++
    }
}

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "Update Complete!" -ForegroundColor Green
Write-Host "Updated: $updatedCount files" -ForegroundColor Cyan
Write-Host "Errors: $errorCount files" -ForegroundColor $(if ($errorCount -eq 0) { "Green" } else { "Red" })
Write-Host "========================================" -ForegroundColor Green
