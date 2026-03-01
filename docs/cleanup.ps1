# SharpCoreDB Documentation Cleanup Script (PowerShell)
# Execute from repository root: .\docs\cleanup.ps1

Write-Host "🧹 SharpCoreDB Documentation Cleanup" -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan
Write-Host ""

# Check if we're in the right directory
if (!(Test-Path "SharpCoreDB.sln")) {
    Write-Host "❌ Error: Must run from repository root" -ForegroundColor Red
    exit 1
}

Write-Host "📁 Step 1: Archive Planning Documents (5 files)" -ForegroundColor Yellow
Write-Host "------------------------------------------------"

$planningFiles = @(
    "docs/graphrag/STRATEGIC_RECOMMENDATIONS.md",
    "docs/graphrag/GRAPHRAG_IMPLEMENTATION_PLAN.md",
    "docs/proposals/DOTMIM_SYNC_IMPLEMENTATION_PLAN.md",
    "docs/proposals/DOTMIM_SYNC_PROVIDER_PROPOSAL.md"
)

foreach ($file in $planningFiles) {
    if (Test-Path $file) {
        $filename = Split-Path $file -Leaf
        git mv $file "docs/archived/planning/$filename" 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✅ Archived $filename" -ForegroundColor Green
        } else {
            Write-Host "⚠️  Error moving $filename" -ForegroundColor Yellow
        }
    } else {
        Write-Host "⚠️  Already moved or missing: $file" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "📁 Step 2: Archive Phase Documents (16 files)" -ForegroundColor Yellow
Write-Host "----------------------------------------------"

$phaseFiles = @(
    # Phase 9
    "docs/graphrag/PHASE9_KICKOFF.md",
    "docs/graphrag/PHASE9_PROGRESS_TRACKING.md",
    "docs/graphrag/PHASE9_STARTED_SUMMARY.md",
    "docs/graphrag/PHASE9_2_IMPLEMENTATION_PLAN.md",
    # Phase 1
    "docs/proposals/PHASE1_DELIVERY.md",
    "docs/proposals/PHASE1_COMPLETION.md",
    "docs/proposals/COMPLETION_SUMMARY.md",
    # Phases 1-6
    "docs/scdb/PHASE1_COMPLETE.md",
    "docs/scdb/PHASE2_COMPLETE.md",
    "docs/scdb/PHASE3_COMPLETE.md",
    "docs/scdb/PHASE4_COMPLETE.md",
    "docs/scdb/PHASE5_COMPLETE.md",
    "docs/scdb/PHASE6_COMPLETE.md",
    # Sync phases
    "docs/sync/PHASE2_COMPLETION.md",
    "docs/sync/PHASE3_COMPLETION.md",
    "docs/sync/PHASE4_COMPLETION.md"
)

foreach ($file in $phaseFiles) {
    if (Test-Path $file) {
        $filename = Split-Path $file -Leaf
        git mv $file "docs/archived/phases/$filename" 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✅ Archived $filename" -ForegroundColor Green
        } else {
            Write-Host "⚠️  Error moving $filename" -ForegroundColor Yellow
        }
    } else {
        Write-Host "⚠️  Already moved or missing: $file" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "🗑️  Step 3: Delete Redundant Documents (5 files)" -ForegroundColor Yellow
Write-Host "------------------------------------------------"

$deleteFiles = @(
    "docs/scdb/IMPLEMENTATION_STATUS.md",
    "docs/graphrag/TEST_EXECUTION_REPORT.md",
    "docs/proposals/ADD_IN_PATTERN_SUMMARY.md",
    "docs/proposals/VISUAL_SUMMARY.md",
    "docs/proposals/QUICK_REFERENCE.md"
)

foreach ($file in $deleteFiles) {
    if (Test-Path $file) {
        $filename = Split-Path $file -Leaf
        git rm $file 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✅ Deleted $filename" -ForegroundColor Green
        } else {
            Write-Host "⚠️  Error deleting $filename" -ForegroundColor Yellow
        }
    } else {
        Write-Host "⚠️  Already deleted or missing: $file" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "📊 Summary" -ForegroundColor Cyan
Write-Host "----------"
$changes = git status --short | Where-Object { $_ -match "(renamed|deleted)" }
Write-Host "Changes staged: $($changes.Count)"

Write-Host ""
Write-Host "✅ Cleanup complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "1. Review changes: git status"
Write-Host "2. Commit: git commit -m 'docs: archive 22 planning docs + cleanup 5 redundant files'"
Write-Host "3. Push: git push origin master"
