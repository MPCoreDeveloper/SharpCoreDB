// <copyright file="RowLevelPolicyModel.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Server.Core.Security;

/// <summary>
/// Defines the operating mode for row-level tenant isolation policies.
/// </summary>
public enum RowLevelPolicyMode
{
    /// <summary>Policy is disabled; no row filtering or write validation occurs.</summary>
    Disabled = 0,

    /// <summary>Policy is enforced; reads are filtered and writes are validated by tenant discriminator.</summary>
    Enforced = 1
}

/// <summary>
/// Represents a row-level isolation policy for a table in shared-database mode.
/// When enforced, the discriminator column is used to filter reads and validate writes
/// so that each tenant sees only its own rows.
/// </summary>
/// <param name="PolicyId">Unique policy identifier.</param>
/// <param name="TenantId">Owning tenant (or '*' for a global policy template).</param>
/// <param name="DatabaseName">Target database name.</param>
/// <param name="TableName">Target table name.</param>
/// <param name="DiscriminatorColumn">Column that holds the tenant identifier in each row.</param>
/// <param name="Mode">Whether the policy is enforced or disabled.</param>
/// <param name="CreatedAt">Policy creation timestamp.</param>
public sealed record RowLevelPolicy(
    string PolicyId,
    string TenantId,
    string DatabaseName,
    string TableName,
    string DiscriminatorColumn,
    RowLevelPolicyMode Mode,
    DateTime CreatedAt)
{
    /// <summary>
    /// Creates a new row-level policy with a generated identifier.
    /// </summary>
    public static RowLevelPolicy Create(
        string tenantId,
        string databaseName,
        string tableName,
        string discriminatorColumn,
        RowLevelPolicyMode mode = RowLevelPolicyMode.Enforced)
    {
        return new RowLevelPolicy(
            PolicyId: Guid.NewGuid().ToString("N"),
            TenantId: tenantId,
            DatabaseName: databaseName,
            TableName: tableName,
            DiscriminatorColumn: discriminatorColumn,
            Mode: mode,
            CreatedAt: DateTime.UtcNow);
    }
}

/// <summary>
/// Result of a row-level policy enforcement decision.
/// </summary>
/// <param name="IsAllowed">Whether the operation is permitted.</param>
/// <param name="DenialReason">Human-readable denial reason, if any.</param>
public sealed record RowLevelPolicyDecision(bool IsAllowed, string? DenialReason = null)
{
    /// <summary>Singleton allowed decision.</summary>
    public static readonly RowLevelPolicyDecision Allowed = new(true);

    /// <summary>Creates a denial decision with the specified reason.</summary>
    public static RowLevelPolicyDecision Denied(string reason) => new(false, reason);
}
