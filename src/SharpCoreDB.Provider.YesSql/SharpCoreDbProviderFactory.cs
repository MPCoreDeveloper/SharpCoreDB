// <copyright file="SharpCoreDbProviderFactory.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Data.Common;
using SharpCoreDB.Data.Provider;

namespace SharpCoreDB.Provider.YesSql;

/// <summary>
/// ADO.NET DbProviderFactory for SharpCoreDB.
/// This allows YesSql to use SharpCoreDB through its standard ADO.NET integration.
/// Delegates to the existing SharpCoreDBProviderFactory from SharpCoreDB.Data.Provider.
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
    /// Delegates to the SharpCoreDB ADO.NET provider.
    /// </summary>
    public override DbConnection CreateConnection()
    {
        return SharpCoreDBProviderFactory.Instance.CreateConnection()
            ?? throw new InvalidOperationException("Failed to create SharpCoreDB connection");
    }

    /// <summary>
    /// Creates a new command object.
    /// Delegates to the SharpCoreDB ADO.NET provider.
    /// </summary>
    public override DbCommand CreateCommand()
    {
        return SharpCoreDBProviderFactory.Instance.CreateCommand()
            ?? throw new InvalidOperationException("Failed to create SharpCoreDB command");
    }

    /// <summary>
    /// Creates a new parameter object.
    /// Delegates to the SharpCoreDB ADO.NET provider.
    /// </summary>
    public override DbParameter CreateParameter()
    {
        return SharpCoreDBProviderFactory.Instance.CreateParameter()
            ?? throw new InvalidOperationException("Failed to create SharpCoreDB parameter");
    }

    /// <summary>
    /// Creates a new connection string builder.
    /// Delegates to the SharpCoreDB ADO.NET provider.
    /// </summary>
    public override DbConnectionStringBuilder CreateConnectionStringBuilder()
    {
        return SharpCoreDBProviderFactory.Instance.CreateConnectionStringBuilder()
            ?? throw new InvalidOperationException("Failed to create SharpCoreDB connection string builder");
    }

    /// <summary>
    /// Indicates that this provider can create data source enumerators.
    /// </summary>
    public override bool CanCreateDataSourceEnumerator => false;
}
