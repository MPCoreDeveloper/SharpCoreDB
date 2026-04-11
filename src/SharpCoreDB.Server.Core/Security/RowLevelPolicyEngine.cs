// <copyright file="RowLevelPolicyEngine.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace SharpCoreDB.Server.Core.Security;

/// <summary>
/// Provides optional row-level tenant isolation for shared-database mode.
/// When policies are registered, reads are filtered and writes are validated
/// so that each tenant sees only its own rows. The engine is disabled by default
/// and does not affect databases without registered policies.
/// C# 14: Uses primary constructor and collection expressions.
/// </summary>
public sealed class RowLevelPolicyEngine(
    ILogger<RowLevelPolicyEngine> logger)
{
    private readonly ILogger<RowLevelPolicyEngine> _logger = logger;

    // Key: "database::table" (case-insensitive)
    private readonly ConcurrentDictionary<string, RowLevelPolicy> _policies = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a row-level policy for a specific table.
    /// </summary>
    /// <param name="policy">The policy to register.</param>
    public void RegisterPolicy(RowLevelPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var key = BuildKey(policy.DatabaseName, policy.TableName);
        _policies[key] = policy;

        _logger.LogInformation(
            "Row-level policy registered for table '{Table}' in database '{Database}' (discriminator='{Column}', mode={Mode})",
            policy.TableName,
            policy.DatabaseName,
            policy.DiscriminatorColumn,
            policy.Mode);
    }

    /// <summary>
    /// Removes a registered row-level policy.
    /// </summary>
    /// <param name="databaseName">Database name.</param>
    /// <param name="tableName">Table name.</param>
    /// <returns>True if a policy was removed.</returns>
    public bool RemovePolicy(string databaseName, string tableName)
    {
        var key = BuildKey(databaseName, tableName);
        return _policies.TryRemove(key, out _);
    }

    /// <summary>
    /// Checks whether a policy is registered and enforced for the given table.
    /// </summary>
    public bool HasEnforcedPolicy(string databaseName, string tableName)
    {
        var key = BuildKey(databaseName, tableName);
        return _policies.TryGetValue(key, out var policy) && policy.Mode == RowLevelPolicyMode.Enforced;
    }

    /// <summary>
    /// Filters query results so that only rows belonging to the requesting tenant are returned.
    /// If no policy is registered for the table, all rows pass through unfiltered.
    /// </summary>
    /// <param name="databaseName">Database name.</param>
    /// <param name="tableName">Table name.</param>
    /// <param name="tenantId">Requesting tenant identifier.</param>
    /// <param name="rows">Unfiltered query results.</param>
    /// <returns>Filtered rows visible to the tenant.</returns>
    public List<Dictionary<string, object>> FilterRows(
        string databaseName,
        string tableName,
        string tenantId,
        List<Dictionary<string, object>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var key = BuildKey(databaseName, tableName);
        if (!_policies.TryGetValue(key, out var policy) || policy.Mode != RowLevelPolicyMode.Enforced)
        {
            return rows;
        }

        var column = policy.DiscriminatorColumn;
        var filtered = new List<Dictionary<string, object>>(rows.Count);

        foreach (var row in rows)
        {
            if (row.TryGetValue(column, out var value) &&
                string.Equals(value?.ToString(), tenantId, StringComparison.Ordinal))
            {
                filtered.Add(row);
            }
        }

        if (filtered.Count < rows.Count)
        {
            _logger.LogDebug(
                "Row-level policy filtered {Removed} of {Total} rows for tenant '{TenantId}' on {Database}.{Table}",
                rows.Count - filtered.Count,
                rows.Count,
                tenantId,
                databaseName,
                tableName);
        }

        return filtered;
    }

    /// <summary>
    /// Validates that a row being written contains the correct tenant discriminator value.
    /// Returns an allowed decision if no policy exists for the table.
    /// </summary>
    /// <param name="databaseName">Database name.</param>
    /// <param name="tableName">Table name.</param>
    /// <param name="tenantId">Requesting tenant identifier.</param>
    /// <param name="row">Row data to validate.</param>
    /// <returns>Policy decision indicating whether the write is permitted.</returns>
    public RowLevelPolicyDecision ValidateWriteRow(
        string databaseName,
        string tableName,
        string tenantId,
        Dictionary<string, object> row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var key = BuildKey(databaseName, tableName);
        if (!_policies.TryGetValue(key, out var policy) || policy.Mode != RowLevelPolicyMode.Enforced)
        {
            return RowLevelPolicyDecision.Allowed;
        }

        var column = policy.DiscriminatorColumn;

        if (!row.TryGetValue(column, out var value))
        {
            _logger.LogWarning(
                "Row-level policy denied write: missing discriminator column '{Column}' for tenant '{TenantId}' on {Database}.{Table}",
                column, tenantId, databaseName, tableName);

            return RowLevelPolicyDecision.Denied(
                $"ROW_POLICY_MISSING_DISCRIMINATOR: column '{column}' is required for row-level tenant isolation.");
        }

        if (!string.Equals(value?.ToString(), tenantId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Row-level policy denied write: discriminator mismatch (expected='{Expected}', actual='{Actual}') on {Database}.{Table}",
                tenantId, value, databaseName, tableName);

            return RowLevelPolicyDecision.Denied(
                $"ROW_POLICY_TENANT_MISMATCH: discriminator value '{value}' does not match session tenant '{tenantId}'.");
        }

        return RowLevelPolicyDecision.Allowed;
    }

    /// <summary>
    /// Validates a SQL write statement by extracting the target table name and checking
    /// whether the session tenant is allowed to write to that table. This is a coarse-grained
    /// check; fine-grained row-value validation happens via <see cref="ValidateWriteRow"/>.
    /// </summary>
    /// <param name="databaseName">Database name.</param>
    /// <param name="sql">The SQL statement being executed.</param>
    /// <param name="tenantId">Requesting tenant identifier.</param>
    /// <returns>Policy decision. Allowed if no enforced policy matches the target table.</returns>
    public RowLevelPolicyDecision ValidateWriteStatement(
        string databaseName,
        string sql,
        string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var tableName = ExtractTargetTable(sql);
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return RowLevelPolicyDecision.Allowed;
        }

        var key = BuildKey(databaseName, tableName);
        if (!_policies.TryGetValue(key, out var policy) || policy.Mode != RowLevelPolicyMode.Enforced)
        {
            return RowLevelPolicyDecision.Allowed;
        }

        // Verify the SQL contains the tenant discriminator value for the session tenant.
        // For INSERT: value must be present in VALUES clause.
        // For UPDATE: WHERE clause must restrict to the tenant's rows.
        var column = policy.DiscriminatorColumn;
        if (!sql.Contains(tenantId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Row-level policy denied write: SQL does not reference tenant '{TenantId}' (table={Table}, database={Database})",
                tenantId, tableName, databaseName);

            return RowLevelPolicyDecision.Denied(
                $"ROW_POLICY_WRITE_DENIED: write to policy-protected table '{tableName}' must include tenant discriminator '{column}' = '{tenantId}'.");
        }

        return RowLevelPolicyDecision.Allowed;
    }

    /// <summary>
    /// Gets all registered policies.
    /// </summary>
    public IReadOnlyCollection<RowLevelPolicy> GetAllPolicies() =>
        _policies.Values.ToList().AsReadOnly();

    /// <summary>
    /// Extracts the target table name from INSERT/UPDATE/DELETE SQL statements.
    /// </summary>
    private static string? ExtractTargetTable(string sql)
    {
        var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            return null;
        }

        var command = parts[0].ToUpperInvariant();
        return command switch
        {
            "INSERT" => parts.Length >= 3 && parts[1].Equals("INTO", StringComparison.OrdinalIgnoreCase)
                ? parts[2].Trim('(', ')', '"', '`', '[', ']')
                : null,
            "UPDATE" => parts[1].Trim('"', '`', '[', ']'),
            "DELETE" => parts.Length >= 3 && parts[1].Equals("FROM", StringComparison.OrdinalIgnoreCase)
                ? parts[2].Trim('"', '`', '[', ']')
                : null,
            _ => null
        };
    }

    private static string BuildKey(string databaseName, string tableName) =>
        $"{databaseName}::{tableName}";
}
