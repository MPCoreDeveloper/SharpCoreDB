// <copyright file="MigrationProgress.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage;

using System;
using System.Collections.Generic;
using SharpCoreDB.Storage.Hybrid;

/// <summary>
/// Tracks the progress and state of a storage mode migration operation.
/// Used for monitoring long-running migrations and providing user feedback.
/// </summary>
public class MigrationProgress
{
    /// <summary>
    /// Gets or sets the unique ID for this migration operation.
    /// </summary>
    public Guid MigrationId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the name of the table being migrated.
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source storage mode.
    /// </summary>
    public StorageMode SourceMode { get; set; }

    /// <summary>
    /// Gets or sets the target storage mode.
    /// </summary>
    public StorageMode TargetMode { get; set; }

    /// <summary>
    /// Gets or sets the current state of the migration.
    /// </summary>
    public MigrationState State { get; set; } = MigrationState.NotStarted;

    /// <summary>
    /// Gets or sets the timestamp when migration started.
    /// </summary>
    public DateTimeOffset? StartTime { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when migration completed (success or failure).
    /// </summary>
    public DateTimeOffset? EndTime { get; set; }

    /// <summary>
    /// Gets or sets the total number of records to migrate.
    /// </summary>
    public long TotalRecords { get; set; }

    /// <summary>
    /// Gets or sets the number of records successfully migrated so far.
    /// </summary>
    public long RecordsMigrated { get; set; }

    /// <summary>
    /// Gets or sets the number of records that failed to migrate.
    /// </summary>
    public long RecordsFailed { get; set; }

    /// <summary>
    /// Gets or sets the size of the source file in bytes.
    /// </summary>
    public long SourceFileSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the size of the target file in bytes (updated during migration).
    /// </summary>
    public long TargetFileSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the current phase of migration.
    /// </summary>
    public string CurrentPhase { get; set; } = "Initializing";

    /// <summary>
    /// Gets or sets any error message if migration failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the exception details if migration failed.
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// Gets or sets the list of warnings encountered during migration.
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Gets or sets the backup file path (for rollback).
    /// </summary>
    public string? BackupFilePath { get; set; }

    /// <summary>
    /// Gets or sets whether verification passed after migration.
    /// </summary>
    public bool? VerificationPassed { get; set; }

    /// <summary>
    /// Gets or sets the checksum of source data.
    /// </summary>
    public uint? SourceChecksum { get; set; }

    /// <summary>
    /// Gets or sets the checksum of target data after migration.
    /// </summary>
    public uint? TargetChecksum { get; set; }

    /// <summary>
    /// Gets the progress percentage (0-100).
    /// </summary>
    public double ProgressPercentage
    {
        get
        {
            if (TotalRecords == 0)
                return 0;

            return Math.Min(100.0, (double)RecordsMigrated / TotalRecords * 100.0);
        }
    }

    /// <summary>
    /// Gets the elapsed time since migration started.
    /// </summary>
    public TimeSpan? ElapsedTime
    {
        get
        {
            if (StartTime == null)
                return null;

            var endTime = EndTime ?? DateTimeOffset.UtcNow;
            return endTime - StartTime.Value;
        }
    }

    /// <summary>
    /// Gets the estimated time remaining based on current progress.
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining
    {
        get
        {
            if (StartTime == null || RecordsMigrated == 0 || TotalRecords == 0)
                return null;

            var elapsed = ElapsedTime;
            if (elapsed == null)
                return null;

            var recordsRemaining = TotalRecords - RecordsMigrated;
            var recordsPerSecond = RecordsMigrated / elapsed.Value.TotalSeconds;

            if (recordsPerSecond <= 0)
                return null;

            return TimeSpan.FromSeconds(recordsRemaining / recordsPerSecond);
        }
    }

    /// <summary>
    /// Gets the migration throughput in records per second.
    /// </summary>
    public double RecordsPerSecond
    {
        get
        {
            var elapsed = ElapsedTime;
            if (elapsed == null || elapsed.Value.TotalSeconds <= 0)
                return 0;

            return RecordsMigrated / elapsed.Value.TotalSeconds;
        }
    }

    /// <summary>
    /// Gets whether the migration is currently active.
    /// </summary>
    public bool IsActive => State == MigrationState.Running || State == MigrationState.Verifying;

    /// <summary>
    /// Gets whether the migration completed successfully.
    /// </summary>
    public bool IsSuccess => State == MigrationState.Completed && VerificationPassed == true;

    /// <summary>
    /// Gets whether the migration failed.
    /// </summary>
    public bool IsFailed => State == MigrationState.Failed || State == MigrationState.RolledBack;

    /// <summary>
    /// Marks the migration as started.
    /// </summary>
    public void MarkStarted()
    {
        State = MigrationState.Running;
        StartTime = DateTimeOffset.UtcNow;
        CurrentPhase = "Reading source data";
    }

    /// <summary>
    /// Marks the migration as completed successfully.
    /// </summary>
    public void MarkCompleted()
    {
        State = MigrationState.Completed;
        EndTime = DateTimeOffset.UtcNow;
        CurrentPhase = "Completed";
    }

    /// <summary>
    /// Marks the migration as failed with an error.
    /// </summary>
    public void MarkFailed(string errorMessage, Exception? exception = null)
    {
        State = MigrationState.Failed;
        EndTime = DateTimeOffset.UtcNow;
        ErrorMessage = errorMessage;
        Exception = exception;
        CurrentPhase = "Failed";
    }

    /// <summary>
    /// Marks the migration as rolled back.
    /// </summary>
    public void MarkRolledBack(string reason)
    {
        State = MigrationState.RolledBack;
        EndTime = DateTimeOffset.UtcNow;
        ErrorMessage = reason;
        CurrentPhase = "Rolled back";
    }

    /// <summary>
    /// Updates the current phase of migration.
    /// </summary>
    public void UpdatePhase(string phase)
    {
        CurrentPhase = phase;
    }

    /// <summary>
    /// Increments the count of migrated records.
    /// </summary>
    public void IncrementMigrated(int count = 1)
    {
        RecordsMigrated += count;
    }

    /// <summary>
    /// Increments the count of failed records.
    /// </summary>
    public void IncrementFailed(int count = 1)
    {
        RecordsFailed += count;
    }

    /// <summary>
    /// Adds a warning message.
    /// </summary>
    public void AddWarning(string warning)
    {
        Warnings.Add($"[{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}] {warning}");
    }

    /// <summary>
    /// Returns a summary of the migration progress.
    /// </summary>
    public override string ToString()
    {
        return $"Migration[{TableName}]: {State} - {RecordsMigrated:N0}/{TotalRecords:N0} ({ProgressPercentage:F1}%) - {CurrentPhase}";
    }

    /// <summary>
    /// Returns a detailed progress report.
    /// </summary>
    public string GetDetailedReport()
    {
        var report = $"""
            ========================================
            Migration Progress Report
            ========================================
            Migration ID:       {MigrationId}
            Table Name:         {TableName}
            Source Mode:        {SourceMode}
            Target Mode:        {TargetMode}
            State:              {State}
            Current Phase:      {CurrentPhase}
            
            Progress:
            --------
            Records Migrated:   {RecordsMigrated:N0} / {TotalRecords:N0} ({ProgressPercentage:F2}%)
            Records Failed:     {RecordsFailed:N0}
            Throughput:         {RecordsPerSecond:F2} records/sec
            
            Timing:
            -------
            Started:            {StartTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Not started"}
            Elapsed:            {ElapsedTime?.ToString(@"hh\:mm\:ss") ?? "N/A"}
            Estimated Remaining:{EstimatedTimeRemaining?.ToString(@"hh\:mm\:ss") ?? "N/A"}
            Ended:              {EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "In progress"}
            
            Files:
            ------
            Source Size:        {SourceFileSizeBytes:N0} bytes ({SourceFileSizeBytes / 1024.0 / 1024.0:F2} MB)
            Target Size:        {TargetFileSizeBytes:N0} bytes ({TargetFileSizeBytes / 1024.0 / 1024.0:F2} MB)
            Backup Path:        {BackupFilePath ?? "None"}
            
            Verification:
            ------------
            Verification:       {GetVerificationStatus()}
            Source Checksum:    {(SourceChecksum.HasValue ? $"0x{SourceChecksum.Value:X8}" : "Not calculated")}
            Target Checksum:    {(TargetChecksum.HasValue ? $"0x{TargetChecksum.Value:X8}" : "Not calculated")}
            Checksums Match:    {GetChecksumMatchStatus()}
            
            Warnings:           {Warnings.Count}
            {(Warnings.Count > 0 ? "\n" + string.Join("\n", Warnings.Take(10)) : "")}
            {(Warnings.Count > 10 ? $"\n... and {Warnings.Count - 10} more warnings" : "")}
            
            {(ErrorMessage != null ? $"\nError: {ErrorMessage}" : "")}
            ========================================
            """;

        return report;
    }

    /// <summary>
    /// Returns a compact one-line status for logging.
    /// </summary>
    public string GetCompactStatus()
    {
        return $"{TableName}: {State} | {RecordsMigrated:N0}/{TotalRecords:N0} ({ProgressPercentage:F1}%) | " +
               $"{RecordsPerSecond:F0} rec/s | {CurrentPhase}";
    }

    /// <summary>
    /// Gets the verification status as a string.
    /// </summary>
    private string GetVerificationStatus()
    {
        if (!VerificationPassed.HasValue)
            return "Pending";
        
        return VerificationPassed.Value ? "PASSED ✓" : "FAILED ✗";
    }

    /// <summary>
    /// Gets the checksum match status as a string.
    /// </summary>
    private string GetChecksumMatchStatus()
    {
        if (!SourceChecksum.HasValue || !TargetChecksum.HasValue)
            return "N/A";
        
        return SourceChecksum.Value == TargetChecksum.Value ? "YES ✓" : "NO ✗";
    }
}

/// <summary>
/// Represents the state of a migration operation.
/// </summary>
public enum MigrationState
{
    /// <summary>
    /// Migration has not started yet.
    /// </summary>
    NotStarted = 0,

    /// <summary>
    /// Migration is currently running.
    /// </summary>
    Running = 1,

    /// <summary>
    /// Migration is verifying data integrity.
    /// </summary>
    Verifying = 2,

    /// <summary>
    /// Migration completed successfully.
    /// </summary>
    Completed = 3,

    /// <summary>
    /// Migration failed with an error.
    /// </summary>
    Failed = 4,

    /// <summary>
    /// Migration was rolled back to original state.
    /// </summary>
    RolledBack = 5,

    /// <summary>
    /// Migration was cancelled by user.
    /// </summary>
    Cancelled = 6
}
