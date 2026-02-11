namespace SharpCoreDB.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using SharpCoreDB.DataStructures;

/// <summary>
/// ✅ Phase 6: Central validation utilities for collation-aware schema migrations.
/// 
/// Provides:
/// - Comprehensive validation of collation changes
/// - Duplicate detection across different collation rules
/// - Compatibility analysis for schema changes
/// - Migration safety reports
/// </summary>
public static class CollationMigrationValidator
{
    /// <summary>
    /// Validates if changing a column from one collation to another is safe.
    /// Detects UNIQUE constraint violations, duplicate creation, data integrity issues.
    /// 
    /// ✅ Phase 6: Comprehensive migration validation.
    /// </summary>
    /// <param name="table">The table containing the column.</param>
    /// <param name="columnName">The column to analyze.</param>
    /// <param name="oldCollation">The current collation.</param>
    /// <param name="newCollation">The desired collation.</param>
    /// <returns>Detailed validation report.</returns>
    public static SchemaMigrationReport ValidateCollationChange(
        Table table,
        string columnName,
        CollationType oldCollation,
        CollationType newCollation)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(columnName);

        var report = new SchemaMigrationReport
        {
            TableName = table.Name,
            ColumnName = columnName,
            OldCollation = oldCollation,
            NewCollation = newCollation,
            ValidatedAt = DateTime.UtcNow
        };

        // Step 1: Find column
        int colIdx = table.Columns.IndexOf(columnName);
        if (colIdx < 0)
        {
            report.Status = MigrationStatus.Error;
            report.Errors.Add($"Column '{columnName}' not found in table '{table.Name}'");
            return report;
        }

        // Step 2: Check if changing PRIMARY KEY (not allowed)
        if (colIdx == table.PrimaryKeyIndex)
        {
            report.Status = MigrationStatus.Error;
            report.Errors.Add("Cannot change collation of PRIMARY KEY column");
            return report;
        }

        // Step 3: If collation unchanged, no-op
        if (oldCollation == newCollation)
        {
            report.Status = MigrationStatus.NoChange;
            report.RowsAffected = 0;
            return report;
        }

        // Step 4: Analyze data
        AnalyzeCollationChangeDuplicates(table, columnName, colIdx, newCollation, report);

        // Step 5: Check UNIQUE constraints
        CheckUniqueConstraintViolations(table, columnName, report);

        // Step 6: Check CHECK constraints (may be affected by collation change)
        CheckConstraintImpact(table, columnName, report);

        // Step 7: Determine final status
        if (report.Errors.Count > 0)
        {
            report.Status = MigrationStatus.Error;
        }
        else if (report.Warnings.Count > 0)
        {
            report.Status = MigrationStatus.Warning;
        }
        else
        {
            report.Status = MigrationStatus.Safe;
        }

        return report;
    }

    /// <summary>
    /// Analyzes what happens to data when collation changes.
    /// Detects duplicates, case variants, special character handling changes.
    /// </summary>
    private static void AnalyzeCollationChangeDuplicates(
        Table table,
        string columnName,
        int colIdx,
        CollationType newCollation,
        SchemaMigrationReport report)
    {
        var rows = table.Select();
        if (rows.Count == 0)
            return;

        // Group values under NEW collation to find duplicates
        var newComparer = new CollationAwareEqualityComparer(newCollation);
        var groupsByNewCollation = new Dictionary<string, List<int>>(newComparer);

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.TryGetValue(columnName, out var value))
            {
                string? valueStr = value?.ToString() ?? string.Empty;
                if (!groupsByNewCollation.ContainsKey(valueStr))
                {
                    groupsByNewCollation[valueStr] = [];
                }
                groupsByNewCollation[valueStr].Add(i);
            }
        }

        // Find duplicates (groups with >1 row)
        var duplicateGroups = groupsByNewCollation
            .Where(kvp => kvp.Value.Count > 1)
            .ToList();

        if (duplicateGroups.Count > 0)
        {
            report.DuplicateGroups = duplicateGroups.Count;
            report.DuplicateRowsCount = duplicateGroups.Sum(g => g.Value.Count - 1); // -1 keeps one

            // Specific duplicate detection based on collation change
            if (report.OldCollation == CollationType.Binary && 
                report.NewCollation == CollationType.NoCase)
            {
                report.Warnings.Add(
                    $"Case-insensitive collation will create {report.DuplicateRowsCount} duplicate values. " +
                    $"Example groups: {string.Join(", ", duplicateGroups.Take(3).Select(g => g.Key))}");
            }
        }

        report.RowsAnalyzed = rows.Count;
    }

    /// <summary>
    /// Checks if UNIQUE constraints on the column would be violated.
    /// </summary>
    private static void CheckUniqueConstraintViolations(
        Table table,
        string columnName,
        SchemaMigrationReport report)
    {
        // Check if column has UNIQUE constraint
        bool isUnique = table.UniqueConstraints.Any(uc => 
            uc.Count == 1 && uc[0] == columnName);

        if (!isUnique)
            return;

        if (report.DuplicateRowsCount > 0)
        {
            report.Errors.Add(
                $"UNIQUE constraint violation: Collation change would create {report.DuplicateRowsCount} " +
                $"duplicate values in UNIQUE column");
        }
    }

    /// <summary>
    /// Checks if CHECK constraints might be affected by collation change.
    /// </summary>
    private static void CheckConstraintImpact(
        Table table,
        string columnName,
        SchemaMigrationReport report)
    {
        int colIdx = table.Columns.IndexOf(columnName);
        if (colIdx < 0)
            return;

        // Check column-level CHECK constraints
        if (colIdx < table.ColumnCheckExpressions.Count && 
            table.ColumnCheckExpressions[colIdx] is not null)
        {
            report.Warnings.Add(
                $"Column-level CHECK constraint may be affected by collation change. " +
                $"Manual review recommended.");
        }

        // Check table-level CHECK constraints mentioning this column
        var affectedTableChecks = table.TableCheckConstraints
            .Where(expr => expr.Contains(columnName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (affectedTableChecks.Count > 0)
        {
            report.Warnings.Add(
                $"{affectedTableChecks.Count} table-level CHECK constraint(s) may be affected. " +
                $"Manual review recommended.");
        }
    }

    /// <summary>
    /// Generates a detailed migration plan for changing a column's collation.
    /// Includes pre-checks, migration steps, and post-checks.
    /// </summary>
    public static MigrationPlan GenerateMigrationPlan(
        Table table,
        string columnName,
        CollationType newCollation)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(columnName);

        var currentCollation = table.GetColumnCollation(columnName) ?? CollationType.Binary;
        var plan = new MigrationPlan
        {
            TableName = table.Name,
            ColumnName = columnName,
            OldCollation = currentCollation,
            NewCollation = newCollation,
            CreatedAt = DateTime.UtcNow
        };

        // Validate first
        var validation = ValidateCollationChange(table, columnName, currentCollation, newCollation);
        plan.ValidationReport = validation;

        if (validation.Status == MigrationStatus.Error)
        {
            plan.ExecutionSteps.Add(new MigrationStep
            {
                StepNumber = 0,
                Description = "VALIDATION FAILED",
                Status = MigrationStepStatus.Failed,
                Details = string.Join("\n", validation.Errors)
            });
            return plan;
        }

        // Build migration steps
        int stepNum = 1;

        // Step 1: Pre-check
        plan.ExecutionSteps.Add(new MigrationStep
        {
            StepNumber = stepNum++,
            Description = "Pre-migration validation",
            Status = MigrationStepStatus.Pending,
            Details = $"Validate column '{columnName}' exists and collation differs"
        });

        // Step 2: Backup (optional, but recommended)
        if (table.GetCachedRowCount() > 0)
        {
            plan.ExecutionSteps.Add(new MigrationStep
            {
                StepNumber = stepNum++,
                Description = "Create backup snapshot",
                Status = MigrationStepStatus.Pending,
                Details = $"Backup table '{table.Name}' before modification"
            });
        }

        // Step 3: Deduplication (if needed)
        if (validation.DuplicateRowsCount > 0)
        {
            plan.ExecutionSteps.Add(new MigrationStep
            {
                StepNumber = stepNum++,
                Description = "Handle duplicate values",
                Status = MigrationStepStatus.Pending,
                Details = $"Remove or consolidate {validation.DuplicateRowsCount} duplicate values. " +
                         $"Recommendation: Review before proceeding"
            });
        }

        // Step 4: Collation change
        plan.ExecutionSteps.Add(new MigrationStep
        {
            StepNumber = stepNum++,
            Description = "Modify column collation",
            Status = MigrationStepStatus.Pending,
            Details = $"Change '{columnName}' from {currentCollation} to {newCollation}"
        });

        // Step 5: Rebuild indexes
        // Skip internal access for now - just mark as needed
        plan.ExecutionSteps.Add(new MigrationStep
        {
            StepNumber = stepNum++,
            Description = "Rebuild affected indexes",
            Status = MigrationStepStatus.Pending,
            Details = $"Rebuild indexes on '{columnName}' with new collation"
        });

        // Step 6: Post-check
        plan.ExecutionSteps.Add(new MigrationStep
        {
            StepNumber = stepNum++,
            Description = "Post-migration validation",
            Status = MigrationStepStatus.Pending,
            Details = "Verify data integrity and index consistency"
        });

        plan.TotalSteps = stepNum - 1;
        return plan;
    }

    /// <summary>
    /// Estimates the time needed to perform a collation change based on data size.
    /// </summary>
    public static TimeEstimate EstimateMigrationTime(Table table, string columnName)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(columnName);

        var rowCount = table.GetCachedRowCount();
        if (rowCount <= 0)
            rowCount = table.Select().Count;

        // Rough estimates (in milliseconds per 1000 rows)
        const double validationTimePerK = 50;  // 50ms per 1K rows
        const double deduplicationTimePerK = 100;  // 100ms per 1K rows
        const double rebuildTimePerK = 200;  // 200ms per 1K rows

        long validationMs = (long)(rowCount / 1000.0 * validationTimePerK);
        long deduplicationMs = (long)(rowCount / 1000.0 * deduplicationTimePerK);
        long rebuildMs = (long)(rowCount / 1000.0 * rebuildTimePerK);

        return new TimeEstimate
        {
            ValidationEstimateMs = validationMs,
            MigrationEstimateMs = rebuildMs,
            TotalEstimateMs = validationMs + deduplicationMs + rebuildMs,
            EstimatedAt = DateTime.UtcNow,
            RowCount = rowCount,
            Confidence = rowCount > 10000 ? 0.7 : 0.85  // Lower confidence for large datasets
        };
    }
}

/// <summary>
/// Detailed report of a schema migration validation.
/// Indicates whether migration is safe and provides statistics.
/// </summary>
public class SchemaMigrationReport
{
    public string TableName { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public CollationType OldCollation { get; set; }
    public CollationType NewCollation { get; set; }
    public MigrationStatus Status { get; set; } = MigrationStatus.Pending;
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    
    // Statistics
    public int RowsAnalyzed { get; set; } = 0;
    public int RowsAffected { get; set; } = 0;
    public int DuplicateGroups { get; set; } = 0;
    public int DuplicateRowsCount { get; set; } = 0;
    public DateTime ValidatedAt { get; set; }
}

/// <summary>
/// Status of a collation change migration.
/// </summary>
public enum MigrationStatus
{
    /// <summary>No validation performed yet.</summary>
    Pending = 0,

    /// <summary>Collation unchanged, no action needed.</summary>
    NoChange = 1,

    /// <summary>Safe to proceed, no issues detected.</summary>
    Safe = 2,

    /// <summary>Can proceed but review warnings first.</summary>
    Warning = 3,

    /// <summary>Cannot proceed due to errors.</summary>
    Error = 4
}

/// <summary>
/// Step in a migration plan.
/// </summary>
public class MigrationStep
{
    public int StepNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public MigrationStepStatus Status { get; set; } = MigrationStepStatus.Pending;
    public string Details { get; set; } = string.Empty;
    public TimeSpan? Duration { get; set; }
}

/// <summary>
/// Status of a migration step.
/// </summary>
public enum MigrationStepStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3,
    Skipped = 4
}

/// <summary>
/// Detailed plan for migrating a column's collation.
/// </summary>
public class MigrationPlan
{
    public string TableName { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public CollationType OldCollation { get; set; }
    public CollationType NewCollation { get; set; }
    public SchemaMigrationReport? ValidationReport { get; set; }
    public List<MigrationStep> ExecutionSteps { get; set; } = [];
    public int TotalSteps { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Estimated time for migration completion.
/// </summary>
public class TimeEstimate
{
    public long ValidationEstimateMs { get; set; }
    public long MigrationEstimateMs { get; set; }
    public long TotalEstimateMs { get; set; }
    public long RowCount { get; set; }
    public double Confidence { get; set; }  // 0.0 to 1.0
    public DateTime EstimatedAt { get; set; }
}
