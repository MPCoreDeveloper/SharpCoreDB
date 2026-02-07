// <copyright file="SharpCoreDbProviderFactory.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Data.Common;
using SharpCoreDB.Data.Provider;

namespace SharpCoreDB.Provider.YesSql;

/// <summary>
/// ADO.NET <see cref="DbProviderFactory"/> for SharpCoreDB.
/// Allows YesSql to use SharpCoreDB through its standard ADO.NET integration.
/// Delegates to <see cref="SharpCoreDBProviderFactory"/> from SharpCoreDB.Data.Provider.
/// </summary>
public sealed class SharpCoreDbProviderFactory : DbProviderFactory
{
    /// <summary>
    /// Singleton instance of the provider factory.
    /// </summary>
    public static readonly SharpCoreDbProviderFactory Instance = new();

    private SharpCoreDbProviderFactory()
    {
    }

    /// <summary>
    /// Creates a new SharpCoreDB connection.
    /// </summary>
    public override DbConnection CreateConnection()
    {
        return SharpCoreDBProviderFactory.Instance.CreateConnection()
            ?? throw new InvalidOperationException("Failed to create SharpCoreDB connection.");
    }

    /// <summary>
    /// Creates a new command object.
    /// </summary>
    public override DbCommand CreateCommand()
    {
        return SharpCoreDBProviderFactory.Instance.CreateCommand()
            ?? throw new InvalidOperationException("Failed to create SharpCoreDB command.");
    }

    /// <summary>
    /// Creates a new parameter object.
    /// </summary>
    public override DbParameter CreateParameter()
    {
        return SharpCoreDBProviderFactory.Instance.CreateParameter()
            ?? throw new InvalidOperationException("Failed to create SharpCoreDB parameter.");
    }

    /// <summary>
    /// Creates a new connection string builder.
    /// </summary>
    public override DbConnectionStringBuilder CreateConnectionStringBuilder()
    {
        return SharpCoreDBProviderFactory.Instance.CreateConnectionStringBuilder()
            ?? throw new InvalidOperationException("Failed to create SharpCoreDB connection string builder.");
    }

    /// <summary>
    /// Creates a new data adapter.
    /// </summary>
    public override DbDataAdapter CreateDataAdapter()
    {
        return SharpCoreDBProviderFactory.Instance.CreateDataAdapter()
            ?? throw new InvalidOperationException("Failed to create SharpCoreDB data adapter.");
    }

    /// <summary>
    /// Creates a new command builder.
    /// </summary>
    public override DbCommandBuilder CreateCommandBuilder()
    {
        return SharpCoreDBProviderFactory.Instance.CreateCommandBuilder()
            ?? throw new InvalidOperationException("Failed to create SharpCoreDB command builder.");
    }

    /// <summary>
    /// Indicates that this provider cannot create data source enumerators.
    /// </summary>
    public override bool CanCreateDataSourceEnumerator => false;
}
