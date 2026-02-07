// <copyright file="SharpCoreDbConnectionFactory.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Data.Common;
using SharpCoreDB.Data.Provider;
using YesSql;

namespace SharpCoreDB.Provider.YesSql;

/// <summary>
/// Connection factory for SharpCoreDB that integrates with YesSql.
/// Creates SharpCoreDB ADO.NET connections for YesSql to use.
/// Implements <see cref="IConnectionFactory"/> for OrchardCore compatibility.
/// </summary>
public sealed class SharpCoreDbConnectionFactory : IConnectionFactory, IDisposable
{
    private volatile string? _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDbConnectionFactory"/> class.
    /// </summary>
    public SharpCoreDbConnectionFactory()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDbConnectionFactory"/> class with a connection string.
    /// </summary>
    /// <param name="connectionString">The connection string.</param>
    public SharpCoreDbConnectionFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <summary>
    /// Sets the connection string for this factory.
    /// Called by YesSql configuration.
    /// </summary>
    /// <param name="connectionString">The connection string.</param>
    public void SetConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <summary>
    /// Creates a new database connection using the SharpCoreDB provider.
    /// YesSql will set the ConnectionString after creation if not already set.
    /// </summary>
    /// <returns>A new SharpCoreDB database connection.</returns>
    public DbConnection CreateConnection()
    {
        var connection = SharpCoreDBProviderFactory.Instance.CreateConnection()
            ?? throw new InvalidOperationException("Failed to create SharpCoreDB connection.");

        if (!string.IsNullOrEmpty(_connectionString))
        {
            connection.ConnectionString = _connectionString;
        }

        return connection;
    }

    /// <summary>
    /// Disposes resources.
    /// No cleanup needed: <see cref="DbProviderFactory"/> is a singleton instance.
    /// Individual connections created by <see cref="CreateConnection"/> are disposed by YesSql.
    /// </summary>
    public void Dispose()
    {
        // No resources to dispose:
        // - DbProviderFactory is a singleton, not owned by this factory
        // - Connections are disposed by YesSql after use
        // - _connectionString is just a string
    }

    /// <summary>
    /// Gets the <see cref="DbProviderFactory"/> for creating connections and commands.
    /// Required by YesSql for ADO.NET operations.
    /// </summary>
    public static DbProviderFactory DbProviderFactory => SharpCoreDBProviderFactory.Instance;

    /// <summary>
    /// Gets the <see cref="DbConnection"/> type for reflection and type checking.
    /// Required by YesSql 5.4.7+ for connection pooling and diagnostics.
    /// </summary>
    public Type DbConnectionType => typeof(SharpCoreDBConnection);
}
