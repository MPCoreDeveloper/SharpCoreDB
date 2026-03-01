@echo off
REM SharpCoreDB Documentation Cleanup Script
REM Execute from repository root

echo Starting documentation cleanup...
echo.

REM Archive planning documents
echo Archiving planning documents...
git mv docs/graphrag/GRAPHRAG_IMPLEMENTATION_PLAN.md docs/archived/planning/ 2>nul
if %errorlevel% equ 0 (echo - Archived GRAPHRAG_IMPLEMENTATION_PLAN.md) else (echo - Already moved or missing)

git mv docs/proposals/DOTMIM_SYNC_IMPLEMENTATION_PLAN.md docs/archived/planning/ 2>nul
if %errorlevel% equ 0 (echo - Archived DOTMIM_SYNC_IMPLEMENTATION_PLAN.md) else (echo - Already moved or missing)

echo.
echo Archiving phase documents...

REM Phase 9 documents
git mv docs/graphrag/PHASE9_KICKOFF.md docs/archived/phases/ 2>nul
if %errorlevel% equ 0 (echo - Archived PHASE9_KICKOFF.md) else (echo - Already moved or missing)

git mv docs/graphrag/PHASE9_PROGRESS_TRACKING.md docs/archived/phases/ 2>nul
if %errorlevel% equ 0 (echo - Archived PHASE9_PROGRESS_TRACKING.md) else (echo - Already moved or missing)

git mv docs/graphrag/PHASE9_STARTED_SUMMARY.md docs/archived/phases/ 2>nul
if %errorlevel% equ 0 (echo - Archived PHASE9_STARTED_SUMMARY.md) else (echo - Already moved or missing)

git mv docs/graphrag/PHASE9_2_IMPLEMENTATION_PLAN.md docs/archived/phases/ 2>nul
if %errorlevel% equ 0 (echo - Archived PHASE9_2_IMPLEMENTATION_PLAN.md) else (echo - Already moved or missing)

REM Phase 1 documents
git mv docs/proposals/PHASE1_DELIVERY.md docs/archived/phases/ 2>nul
if %errorlevel% equ 0 (echo - Archived PHASE1_DELIVERY.md) else (echo - Already moved or missing)

git mv docs/proposals/PHASE1_COMPLETION.md docs/archived/phases/ 2>nul
if %errorlevel% equ 0 (echo - Archived PHASE1_COMPLETION.md) else (echo - Already moved or missing)

git mv docs/proposals/COMPLETION_SUMMARY.md docs/archived/phases/ 2>nul
if %errorlevel% equ 0 (echo - Archived COMPLETION_SUMMARY.md) else (echo - Already moved or missing)

REM Phases 1-6 SCDB documents
git mv docs/scdb/PHASE1_COMPLETE.md docs/archived/phases/ 2>nul
if %errorlevel% equ 0 (echo - Archived PHASE1_COMPLETE.md) else (echo - Already moved or missing)

git mv docs/scdb/PHASE2_COMPLETE.md docs/archived/phases/ 2>nul
if %errorlevel% equ 0 (echo - Archived PHASE2_COMPLETE.md) else (echo - Already moved or missing)

git mv docs/scdb/PHASE3_COMPLETE.md docs/archived/phases/ 2>nul
if %errorlevel% equ 0 (echo - Archived PHASE3_COMPLETE.md) else (echo - Already moved or missing)

git mv docs/scdb/PHASE4_COMPLETE.md docs/archived/phases/ 2>nul
if %errorlevel% equ 0 (echo - Archived PHASE4_COMPLETE.md) else (echo - Already moved or missing)

git mv docs/scdb/PHASE5_COMPLETE.md docs/archived/phases/ 2>nul
if %errorlevel% equ 0 (echo - Archived PHASE5_COMPLETE.md) else (echo - Already moved or missing)

git mv docs/scdb/PHASE6_COMPLETE.md docs/archived/phases/ 2>nul
if %errorlevel% equ 0 (echo - Archived PHASE6_COMPLETE.md) else (echo - Already moved or missing)

REM Sync phase documents
git mv docs/sync/PHASE2_COMPLETION.md docs/archived/phases/ 2>nul
if %errorlevel% equ 0 (echo - Archived PHASE2_COMPLETION.md) else (echo - Already moved or missing)

git mv docs/sync/PHASE3_COMPLETION.md docs/archived/phases/ 2>nul
if %errorlevel% equ 0 (echo - Archived PHASE3_COMPLETION.md) else (echo - Already moved or missing)

git mv docs/sync/PHASE4_COMPLETION.md docs/archived/phases/ 2>nul
if %errorlevel% equ 0 (echo - Archived PHASE4_COMPLETION.md) else (echo - Already moved or missing)

echo.
echo Deleting redundant documents...

REM Delete redundant files
git rm docs/scdb/IMPLEMENTATION_STATUS.md 2>nul
if %errorlevel% equ 0 (echo - Deleted IMPLEMENTATION_STATUS.md) else (echo - Already deleted or missing)

git rm docs/graphrag/TEST_EXECUTION_REPORT.md 2>nul
if %errorlevel% equ 0 (echo - Deleted TEST_EXECUTION_REPORT.md) else (echo - Already deleted or missing)

git rm docs/proposals/ADD_IN_PATTERN_SUMMARY.md 2>nul
if %errorlevel% equ 0 (echo - Deleted ADD_IN_PATTERN_SUMMARY.md) else (echo - Already deleted or missing)

git rm docs/proposals/VISUAL_SUMMARY.md 2>nul
if %errorlevel% equ 0 (echo - Deleted VISUAL_SUMMARY.md) else (echo - Already deleted or missing)

git rm docs/proposals/QUICK_REFERENCE.md 2>nul
if %errorlevel% equ 0 (echo - Deleted QUICK_REFERENCE.md) else (echo - Already deleted or missing)

echo.
echo Cleanup complete!
echo.
echo Next steps:
echo 1. Review changes: git status
echo 2. Add new files: git add docs/
echo 3. Commit: git commit -m "docs: archive 21 planning docs + cleanup 5 redundant files"
echo 4. Push: git push origin master
