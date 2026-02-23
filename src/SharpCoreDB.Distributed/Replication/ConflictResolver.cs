// <copyright file="ConflictResolver.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Resolves conflicts that occur during replication when the same data is modified on different replicas.
/// Supports multiple resolution strategies and custom conflict resolvers.
/// C# 14: Primary constructors, pattern matching for strategy selection.
/// </summary>
public sealed class ConflictResolver
{
    private readonly ILogger<ConflictResolver>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictResolver"/> class.
    /// </summary>
    /// <param name="logger">Optional logger for conflict resolution events.</param>
    public ConflictResolver(ILogger<ConflictResolver>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Resolves a conflict between two conflicting data versions.
    /// </summary>
    /// <param name="conflict">The conflict to resolve.</param>
    /// <param name="strategy">The resolution strategy to use.</param>
    /// <returns>The resolved data version.</returns>
    public ConflictResolution ResolveConflict(DataConflict conflict, ConflictResolutionStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(conflict);

        _logger?.LogInformation("Resolving conflict for {Table}.{Column} using strategy {Strategy}",
            conflict.TableName, conflict.ColumnName, strategy);

        var resolution = strategy switch
        {
            ConflictResolutionStrategy.LastWriteWins => ResolveLastWriteWins(conflict),
            ConflictResolutionStrategy.FirstWriteWins => ResolveFirstWriteWins(conflict),
            ConflictResolutionStrategy.Merge => ResolveMerge(conflict),
            ConflictResolutionStrategy.Custom => ResolveCustom(conflict),
            ConflictResolutionStrategy.Manual => ResolveManual(conflict),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unknown conflict resolution strategy")
        };

        _logger?.LogInformation("Conflict resolved: {Winner} wins with value {Value}",
            resolution.WinnerReplicaId, resolution.ResolvedValue);

        return resolution;
    }

    /// <summary>
    /// Resolves conflict using last-write-wins strategy.
    /// </summary>
    /// <param name="conflict">The conflict to resolve.</param>
    /// <returns>The resolution result.</returns>
    private static ConflictResolution ResolveLastWriteWins(DataConflict conflict)
    {
        var latestVersion = conflict.ConflictingVersions
            .MaxBy(v => v.Timestamp)!;

        return new ConflictResolution
        {
            WinnerReplicaId = latestVersion.ReplicaId,
            ResolvedValue = latestVersion.Value,
            StrategyUsed = ConflictResolutionStrategy.LastWriteWins,
            ResolutionTimestamp = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Resolves conflict using first-write-wins strategy.
    /// </summary>
    /// <param name="conflict">The conflict to resolve.</param>
    /// <returns>The resolution result.</returns>
    private static ConflictResolution ResolveFirstWriteWins(DataConflict conflict)
    {
        var earliestVersion = conflict.ConflictingVersions
            .MinBy(v => v.Timestamp)!;

        return new ConflictResolution
        {
            WinnerReplicaId = earliestVersion.ReplicaId,
            ResolvedValue = earliestVersion.Value,
            StrategyUsed = ConflictResolutionStrategy.FirstWriteWins,
            ResolutionTimestamp = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Resolves conflict using merge strategy (for compatible changes).
    /// </summary>
    /// <param name="conflict">The conflict to resolve.</param>
    /// <returns>The resolution result.</returns>
    private static ConflictResolution ResolveMerge(DataConflict conflict)
    {
        // For simple cases, prefer non-null values
        var nonNullVersions = conflict.ConflictingVersions
            .Where(v => v.Value is not null)
            .ToList();

        if (nonNullVersions.Count == 1)
        {
            return new ConflictResolution
            {
                WinnerReplicaId = nonNullVersions[0].ReplicaId,
                ResolvedValue = nonNullVersions[0].Value,
                StrategyUsed = ConflictResolutionStrategy.Merge,
                ResolutionTimestamp = DateTimeOffset.UtcNow
            };
        }

        // For complex merges, use last-write-wins as fallback
        var lwwResolution = ResolveLastWriteWins(conflict);
        return new ConflictResolution
        {
            WinnerReplicaId = lwwResolution.WinnerReplicaId,
            ResolvedValue = lwwResolution.ResolvedValue,
            StrategyUsed = ConflictResolutionStrategy.Merge,
            ResolutionTimestamp = lwwResolution.ResolutionTimestamp
        };
    }

    /// <summary>
    /// Resolves conflict using custom resolution logic.
    /// </summary>
    /// <param name="conflict">The conflict to resolve.</param>
    /// <returns>The resolution result.</returns>
    private static ConflictResolution ResolveCustom(DataConflict conflict)
    {
        // In a real implementation, this would invoke custom resolver logic
        // For now, fall back to last-write-wins
        var lwwResolution = ResolveLastWriteWins(conflict);
        return new ConflictResolution
        {
            WinnerReplicaId = lwwResolution.WinnerReplicaId,
            ResolvedValue = lwwResolution.ResolvedValue,
            StrategyUsed = ConflictResolutionStrategy.Custom,
            ResolutionTimestamp = lwwResolution.ResolutionTimestamp
        };
    }

    /// <summary>
    /// Resolves conflict requiring manual intervention.
    /// </summary>
    /// <param name="conflict">The conflict to resolve.</param>
    /// <returns>The resolution result.</returns>
    private static ConflictResolution ResolveManual(DataConflict conflict)
    {
        // Manual resolution - mark for human intervention
        return new ConflictResolution
        {
            WinnerReplicaId = "manual",
            ResolvedValue = conflict.ConflictingVersions.First().Value, // Placeholder
            StrategyUsed = ConflictResolutionStrategy.Manual,
            ResolutionTimestamp = DateTimeOffset.UtcNow,
            RequiresManualIntervention = true
        };
    }

    /// <summary>
    /// Validates if a conflict can be automatically resolved.
    /// </summary>
    /// <param name="conflict">The conflict to validate.</param>
    /// <param name="strategy">The resolution strategy.</param>
    /// <returns>True if the conflict can be automatically resolved.</returns>
    public bool CanAutoResolve(DataConflict conflict, ConflictResolutionStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(conflict);

        return strategy switch
        {
            ConflictResolutionStrategy.LastWriteWins or
            ConflictResolutionStrategy.FirstWriteWins => true,
            ConflictResolutionStrategy.Merge => CanMerge(conflict),
            ConflictResolutionStrategy.Custom => true, // Assume custom resolver can handle it
            ConflictResolutionStrategy.Manual => false,
            _ => false
        };
    }

    /// <summary>
    /// Determines if a conflict can be resolved through merging.
    /// </summary>
    /// <param name="conflict">The conflict to check.</param>
    /// <returns>True if the conflict can be merged.</returns>
    private static bool CanMerge(DataConflict conflict)
    {
        // Simple merge logic: if only one version has a non-null value
        var nonNullCount = conflict.ConflictingVersions.Count(v => v.Value is not null);
        return nonNullCount <= 1;
    }
}

/// <summary>
/// Represents a data conflict during replication.
/// </summary>
public class DataConflict
{
    /// <summary>Gets the table name where the conflict occurred.</summary>
    public required string TableName { get; init; }

    /// <summary>Gets the column name where the conflict occurred.</summary>
    public required string ColumnName { get; init; }

    /// <summary>Gets the primary key value identifying the row.</summary>
    public required object PrimaryKey { get; init; }

    /// <summary>Gets the conflicting data versions from different replicas.</summary>
    public required IReadOnlyList<DataVersion> ConflictingVersions { get; init; }

    /// <summary>Gets the timestamp when the conflict was detected.</summary>
    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Represents a version of data from a specific replica.
/// </summary>
public class DataVersion
{
    /// <summary>Gets the replica identifier.</summary>
    public required string ReplicaId { get; init; }

    /// <summary>Gets the data value.</summary>
    public required object? Value { get; init; }

    /// <summary>Gets the timestamp of the change.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the transaction identifier.</summary>
    public string? TransactionId { get; init; }
}

/// <summary>
/// Result of a conflict resolution.
/// </summary>
public class ConflictResolution
{
    /// <summary>Gets the replica ID that won the conflict.</summary>
    public required string WinnerReplicaId { get; init; }

    /// <summary>Gets the resolved data value.</summary>
    public required object? ResolvedValue { get; init; }

    /// <summary>Gets the strategy used for resolution.</summary>
    public required ConflictResolutionStrategy StrategyUsed { get; init; }

    /// <summary>Gets the timestamp when the conflict was resolved.</summary>
    public required DateTimeOffset ResolutionTimestamp { get; init; }

    /// <summary>Gets whether manual intervention is required.</summary>
    public bool RequiresManualIntervention { get; init; }
}

/// <summary>
/// Conflict resolution strategies.
/// </summary>
public enum ConflictResolutionStrategy
{
    /// <summary>Last write wins - most recent change takes precedence.</summary>
    LastWriteWins,

    /// <summary>First write wins - earliest change takes precedence.</summary>
    FirstWriteWins,

    /// <summary>Merge compatible changes.</summary>
    Merge,

    /// <summary>Use custom resolution logic.</summary>
    Custom,

    /// <summary>Require manual intervention.</summary>
    Manual
}
