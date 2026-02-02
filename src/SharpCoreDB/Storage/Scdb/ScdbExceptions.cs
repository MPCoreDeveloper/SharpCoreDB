// <copyright file="ScdbExceptions.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Scdb;

using System;
using System.IO;

/// <summary>
/// Base exception for SCDB-related errors.
/// </summary>
public class ScdbException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScdbException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public ScdbException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScdbException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ScdbException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when SCDB file corruption is detected.
/// </summary>
public sealed class ScdbCorruptionException : ScdbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScdbCorruptionException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="severity">The corruption severity.</param>
    /// <param name="offset">The file offset where corruption was detected.</param>
    /// <param name="blockName">The block name if applicable.</param>
    public ScdbCorruptionException(
        string message,
        CorruptionSeverity severity = CorruptionSeverity.Severe,
        long? offset = null,
        string? blockName = null)
        : base(BuildDetailedMessage(message, severity, offset, blockName))
    {
        Severity = severity;
        Offset = offset;
        BlockName = blockName;
    }

    /// <summary>
    /// Gets the corruption severity.
    /// </summary>
    public CorruptionSeverity Severity { get; }

    /// <summary>
    /// Gets the file offset where corruption was detected.
    /// </summary>
    public long? Offset { get; }

    /// <summary>
    /// Gets the block name if applicable.
    /// </summary>
    public string? BlockName { get; }

    private static string BuildDetailedMessage(
        string message,
        CorruptionSeverity severity,
        long? offset,
        string? blockName)
    {
        var details = $"SCDB Corruption Detected ({severity}):\n{message}";

        if (offset.HasValue)
        {
            details += $"\n- File Offset: 0x{offset.Value:X8}";
        }

        if (blockName != null)
        {
            details += $"\n- Block: {blockName}";
        }

        details += "\n\nRecommended Action:";
        details += severity switch
        {
            CorruptionSeverity.Warning => "\n- Monitor the situation, database is still usable",
            CorruptionSeverity.Moderate => "\n- Run CorruptionDetector.ValidateAsync() for full analysis\n- Consider repair with RepairTool",
            CorruptionSeverity.Severe => "\n- STOP writing to database\n- Run RepairTool.RepairAsync() immediately\n- Restore from backup if repair fails",
            CorruptionSeverity.Critical => "\n- Database is UNUSABLE\n- Restore from backup\n- Do NOT attempt repair without backup",
            _ => "\n- Run CorruptionDetector.ValidateAsync() for analysis"
        };

        return details;
    }
}

/// <summary>
/// Exception thrown for recoverable SCDB errors that can be fixed automatically.
/// </summary>
public sealed class ScdbRecoverableException : ScdbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScdbRecoverableException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="repairSuggestion">Suggested repair action.</param>
    public ScdbRecoverableException(string message, string repairSuggestion)
        : base($"{message}\n\nRepair Suggestion: {repairSuggestion}")
    {
        RepairSuggestion = repairSuggestion;
    }

    /// <summary>
    /// Gets the repair suggestion.
    /// </summary>
    public string RepairSuggestion { get; }
}

/// <summary>
/// Exception thrown for unrecoverable SCDB errors requiring backup restore.
/// </summary>
public sealed class ScdbUnrecoverableException : ScdbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScdbUnrecoverableException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="backupRecommendation">Backup restore recommendation.</param>
    public ScdbUnrecoverableException(string message, string backupRecommendation)
        : base($"{message}\n\nBackup Recommendation: {backupRecommendation}")
    {
        BackupRecommendation = backupRecommendation;
    }

    /// <summary>
    /// Gets the backup restore recommendation.
    /// </summary>
    public string BackupRecommendation { get; }
}

/// <summary>
/// Exception thrown when SCDB file format is invalid or unsupported.
/// </summary>
public sealed class ScdbFormatException : ScdbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScdbFormatException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="expectedVersion">The expected format version.</param>
    /// <param name="actualVersion">The actual format version.</param>
    public ScdbFormatException(string message, int? expectedVersion = null, int? actualVersion = null)
        : base(BuildVersionMessage(message, expectedVersion, actualVersion))
    {
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    /// <summary>
    /// Gets the expected format version.
    /// </summary>
    public int? ExpectedVersion { get; }

    /// <summary>
    /// Gets the actual format version.
    /// </summary>
    public int? ActualVersion { get; }

    private static string BuildVersionMessage(string message, int? expected, int? actual)
    {
        var details = $"SCDB Format Error: {message}";

        if (expected.HasValue && actual.HasValue)
        {
            details += $"\n- Expected Version: {expected.Value}";
            details += $"\n- Actual Version: {actual.Value}";

            if (actual.Value > expected.Value)
            {
                details += "\n\nThis file was created with a newer version of SharpCoreDB.";
                details += "\nPlease upgrade to the latest version to open this file.";
            }
            else
            {
                details += "\n\nThis file format is obsolete.";
                details += "\nUse ScdbMigrator to upgrade the file to the current format.";
            }
        }

        return details;
    }
}

/// <summary>
/// Exception thrown when SCDB operation times out.
/// </summary>
public sealed class ScdbTimeoutException : TimeoutException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScdbTimeoutException"/> class.
    /// </summary>
    /// <param name="operation">The operation that timed out.</param>
    /// <param name="timeout">The timeout duration.</param>
    public ScdbTimeoutException(string operation, TimeSpan timeout)
        : base($"SCDB operation '{operation}' timed out after {timeout.TotalSeconds:F1}s.\n\n" +
               $"Possible causes:\n" +
               $"- File system is slow or unresponsive\n" +
               $"- Large transaction blocking I/O\n" +
               $"- Insufficient system resources\n\n" +
               $"Recommendations:\n" +
               $"- Increase timeout in DatabaseOptions\n" +
               $"- Check disk performance\n" +
               $"- Reduce transaction size")
    {
        Operation = operation;
        Timeout = timeout;
    }

    /// <summary>
    /// Gets the operation that timed out.
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// Gets the timeout duration.
    /// </summary>
    public TimeSpan Timeout { get; }
}
