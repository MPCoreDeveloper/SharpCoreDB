// <copyright file="SingleFileTableFactory.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Storage;

using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;

/// <summary>
/// <see cref="ITableFactory"/> for single-file (.scdb) databases.
/// Creates <see cref="SingleFileTable"/> instances backed by an <see cref="IStorageProvider"/>,
/// allowing <see cref="SharpCoreDB.Services.SqlParser"/> DDL to create tables in single-file mode
/// without a filesystem-backed <see cref="IStorage"/> provider.
/// </summary>
internal sealed class SingleFileTableFactory(IStorageProvider storageProvider) : ITableFactory
{
    private readonly IStorageProvider _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));

    /// <inheritdoc />
    /// <remarks>
    /// The <paramref name="tableName"/> is used as the storage block identifier within the
    /// single .scdb file. Schema properties are populated by the DDL caller after construction.
    /// <paramref name="config"/> is forwarded to the table so type mapping (e.g.
    /// <see cref="DatabaseConfig.UseSqliteIntegerAffinity"/>) is honored by ALTER TABLE
    /// ADD COLUMN in single-file mode.
    /// </remarks>
    public ITable CreateTable(string tableName, bool isReadOnly, DatabaseConfig? config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        return new SingleFileTable(tableName, _storageProvider, config);
    }
}
