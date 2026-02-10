#!/usr/bin/env pwsh
# Script to create the COLLATE support GitHub issue
# Run this after authenticating with: gh auth login

$ErrorActionPreference = "Stop"

# Check authentication
try {
    gh auth status 2>&1 | Out-Null
} catch {
    Write-Host "⚠️  Not authenticated. Running: gh auth login" -ForegroundColor Yellow
    gh auth login -p https -h github.com -w
}

# Create the issue
$title = "feat: SQL COLLATE support for case-insensitive and locale-aware string comparisons"
$body = Get-Content -Path "docs/COLLATE_ISSUE_BODY.md" -Raw

gh issue create `
    --repo "MPCoreDeveloper/SharpCoreDB" `
    --title $title `
    --body $body `
    --label "enhancement"

Write-Host "✅ Issue created successfully!" -ForegroundColor Green

# Clean up the issue body file (optional)
Remove-Item "docs/COLLATE_ISSUE_BODY.md" -ErrorAction SilentlyContinue
git add -A
git commit -m "docs: clean up issue body temp file" --allow-empty 2>$null
