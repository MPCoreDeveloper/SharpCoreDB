// <copyright file="MasterDatabaseLocator.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Server.Core;

/// <summary>
/// Resolves the database that holds the master-database repositories (tenant catalog, database grants).
/// The two candidate names are the configured system-database name and the server's default database;
/// when neither is registered the caller gets a diagnostic that names what was tried, so a
/// misconfiguration is actionable instead of a bare "unable to resolve".
/// </summary>
public static class MasterDatabaseLocator
{
    /// <summary>
    /// Tries to resolve the master database from the registry without throwing.
    /// </summary>
    /// <param name="registry">The database registry to resolve from.</param>
    /// <param name="systemDatabaseName">The configured system-database name (may be empty).</param>
    /// <param name="defaultDatabaseName">The configured default database name (may be empty).</param>
    /// <returns>The resolved database instance, or <c>null</c> when neither candidate is registered.</returns>
    public static DatabaseInstance? TryResolve(
        DatabaseRegistry registry,
        string? systemDatabaseName,
        string? defaultDatabaseName)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return Resolve(registry, systemDatabaseName) ?? Resolve(registry, defaultDatabaseName);
    }

    /// <summary>
    /// Builds the message for a failed resolution: the candidates that were tried, the databases the
    /// registry actually holds, and whether the registry finished initializing.
    /// </summary>
    /// <param name="registry">The database registry the resolution ran against.</param>
    /// <param name="systemDatabaseName">The configured system-database name (may be empty).</param>
    /// <param name="defaultDatabaseName">The configured default database name (may be empty).</param>
    /// <returns>An actionable diagnostic message.</returns>
    public static string DescribeUnresolved(
        DatabaseRegistry registry,
        string? systemDatabaseName,
        string? defaultDatabaseName)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var registered = registry.DatabaseNames.Count == 0
            ? "<none>"
            : string.Join(", ", registry.DatabaseNames);

        return $"Unable to resolve the master database for the tenant catalog and database grants repositories. " +
               $"Tried the configured system database '{Describe(systemDatabaseName)}' and the default database " +
               $"'{Describe(defaultDatabaseName)}'. The registry {(registry.IsInitialized ? "is" : "is not")} " +
               $"initialized and holds: {registered}. Configure a matching name in " +
               $"Server:SystemDatabases:MasterDatabaseName or Server:DefaultDatabase, or add the database to " +
               $"the Server:Databases list.";
    }

    private static DatabaseInstance? Resolve(DatabaseRegistry registry, string? databaseName) =>
        string.IsNullOrWhiteSpace(databaseName) ? null : registry.GetDatabase(databaseName);

    private static string Describe(string? databaseName) =>
        string.IsNullOrWhiteSpace(databaseName) ? "<not configured>" : databaseName;
}
