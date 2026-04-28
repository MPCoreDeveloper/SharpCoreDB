// <copyright file="DirectoryTableFactory.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Storage;

using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;

/// <summary>
/// <see cref="ITableFactory"/> for directory-mode databases.
/// Creates <see cref="Table"/> instances backed by an <see cref="IStorage"/> provider,
/// preserving the existing directory-mode behaviour exactly.
/// </summary>
internal sealed class DirectoryTableFactory(IStorage storage) : ITableFactory
{
    private readonly IStorage _storage = storage;

    /// <inheritdoc />
    public ITable CreateTable(string tableName, bool isReadOnly, DatabaseConfig? config)
        => new Table(_storage, isReadOnly, config);
}
