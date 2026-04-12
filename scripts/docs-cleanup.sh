#!/bin/bash
# SharpCoreDB Documentation Cleanup Script
# Execute from repository root: bash docs/cleanup.sh

echo "🧹 SharpCoreDB Documentation Cleanup"
echo "===================================="
echo ""

# Check if we're in the right directory
if [ ! -f "SharpCoreDB.slnx" ]; then
    echo "❌ Error: Must run from repository root"
    exit 1
fi

echo "📁 Step 1: Archive Planning Documents (6 files)"
echo "------------------------------------------------"

git mv docs/graphrag/STRATEGIC_RECOMMENDATIONS.md docs/archived/planning/ 2>/dev/null && echo "✅ Archived STRATEGIC_RECOMMENDATIONS.md" || echo "⚠️  Already moved or missing"
git mv docs/graphrag/GRAPHRAG_IMPLEMENTATION_PLAN.md docs/archived/planning/ 2>/dev/null && echo "✅ Archived GRAPHRAG_IMPLEMENTATION_PLAN.md" || echo "⚠️  Already moved or missing"
git mv docs/proposals/DOTMIM_SYNC_IMPLEMENTATION_PLAN.md docs/archived/planning/ 2>/dev/null && echo "✅ Archived DOTMIM_SYNC_IMPLEMENTATION_PLAN.md" || echo "⚠️  Already moved or missing"
git mv docs/proposals/DOTMIM_SYNC_PROVIDER_PROPOSAL.md docs/archived/planning/ 2>/dev/null && echo "✅ Archived DOTMIM_SYNC_PROVIDER_PROPOSAL.md" || echo "⚠️  Already moved or missing"

echo ""
echo "📁 Step 2: Archive Phase Documents (16 files)"
echo "----------------------------------------------"

# Phase 9 documents
git mv docs/graphrag/PHASE9_KICKOFF.md docs/archived/phases/ 2>/dev/null && echo "✅ Archived PHASE9_KICKOFF.md" || echo "⚠️  Already moved or missing"
git mv docs/graphrag/PHASE9_PROGRESS_TRACKING.md docs/archived/phases/ 2>/dev/null && echo "✅ Archived PHASE9_PROGRESS_TRACKING.md" || echo "⚠️  Already moved or missing"
git mv docs/graphrag/PHASE9_STARTED_SUMMARY.md docs/archived/phases/ 2>/dev/null && echo "✅ Archived PHASE9_STARTED_SUMMARY.md" || echo "⚠️  Already moved or missing"
git mv docs/graphrag/PHASE9_2_IMPLEMENTATION_PLAN.md docs/archived/phases/ 2>/dev/null && echo "✅ Archived PHASE9_2_IMPLEMENTATION_PLAN.md" || echo "⚠️  Already moved or missing"

# Phase 1 documents
git mv docs/proposals/PHASE1_DELIVERY.md docs/archived/phases/ 2>/dev/null && echo "✅ Archived PHASE1_DELIVERY.md" || echo "⚠️  Already moved or missing"
git mv docs/proposals/PHASE1_COMPLETION.md docs/archived/phases/ 2>/dev/null && echo "✅ Archived PHASE1_COMPLETION.md" || echo "⚠️  Already moved or missing"
git mv docs/proposals/COMPLETION_SUMMARY.md docs/archived/phases/ 2>/dev/null && echo "✅ Archived COMPLETION_SUMMARY.md" || echo "⚠️  Already moved or missing"

# Phase 1-6 SCDB documents
git mv docs/scdb/PHASE1_COMPLETE.md docs/archived/phases/ 2>/dev/null && echo "✅ Archived PHASE1_COMPLETE.md" || echo "⚠️  Already moved or missing"
git mv docs/scdb/PHASE2_COMPLETE.md docs/archived/phases/ 2>/dev/null && echo "✅ Archived PHASE2_COMPLETE.md" || echo "⚠️  Already moved or missing"
git mv docs/scdb/PHASE3_COMPLETE.md docs/archived/phases/ 2>/dev/null && echo "✅ Archived PHASE3_COMPLETE.md" || echo "⚠️  Already moved or missing"
git mv docs/scdb/PHASE4_COMPLETE.md docs/archived/phases/ 2>/dev/null && echo "✅ Archived PHASE4_COMPLETE.md" || echo "⚠️  Already moved or missing"
git mv docs/scdb/PHASE5_COMPLETE.md docs/archived/phases/ 2>/dev/null && echo "✅ Archived PHASE5_COMPLETE.md" || echo "⚠️  Already moved or missing"
git mv docs/scdb/PHASE6_COMPLETE.md docs/archived/phases/ 2>/dev/null && echo "✅ Archived PHASE6_COMPLETE.md" || echo "⚠️  Already moved or missing"

# Sync phase documents
git mv docs/sync/PHASE2_COMPLETION.md docs/archived/phases/ 2>/dev/null && echo "✅ Archived PHASE2_COMPLETION.md" || echo "⚠️  Already moved or missing"
git mv docs/sync/PHASE3_COMPLETION.md docs/archived/phases/ 2>/dev/null && echo "✅ Archived PHASE3_COMPLETION.md" || echo "⚠️  Already moved or missing"
git mv docs/sync/PHASE4_COMPLETION.md docs/archived/phases/ 2>/dev/null && echo "✅ Archived PHASE4_COMPLETION.md" || echo "⚠️  Already moved or missing"

echo ""
echo "🗑️  Step 3: Delete Redundant Documents (5 files)"
echo "------------------------------------------------"

git rm docs/scdb/IMPLEMENTATION_STATUS.md 2>/dev/null && echo "✅ Deleted IMPLEMENTATION_STATUS.md" || echo "⚠️  Already deleted or missing"
git rm docs/graphrag/TEST_EXECUTION_REPORT.md 2>/dev/null && echo "✅ Deleted TEST_EXECUTION_REPORT.md" || echo "⚠️  Already deleted or missing"
git rm docs/proposals/ADD_IN_PATTERN_SUMMARY.md 2>/dev/null && echo "✅ Deleted ADD_IN_PATTERN_SUMMARY.md" || echo "⚠️  Already deleted or missing"
git rm docs/proposals/VISUAL_SUMMARY.md 2>/dev/null && echo "✅ Deleted VISUAL_SUMMARY.md" || echo "⚠️  Already deleted or missing"
git rm docs/proposals/QUICK_REFERENCE.md 2>/dev/null && echo "✅ Deleted QUICK_REFERENCE.md" || echo "⚠️  Already deleted or missing"

echo ""
echo "📊 Summary"
echo "----------"
git status --short | grep -E "(renamed|deleted)" | wc -l | xargs echo "Changes staged:"

echo ""
echo "✅ Cleanup complete!"
echo ""
echo "Next steps:"
echo "1. Review changes: git status"
echo "2. Commit: git commit -m 'docs: archive 22 planning docs + cleanup 5 redundant files'"
echo "3. Push: git push origin master"
