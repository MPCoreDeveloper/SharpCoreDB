// <copyright file="ITableFactory.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Interfaces;

using SharpCoreDB.DataStructures;

/// <summary>
/// Abstracts table creation so that <see cref="SharpCoreDB.Services.SqlParser"/> can produce
/// either a directory-mode <see cref="SharpCoreDB.DataStructures.Table"/> or a single-file
/// <see cref="SharpCoreDB.SingleFileTable"/> without being coupled to a concrete storage type.
/// </summary>
/// <remarks>
/// This interface is the key backward-compatibility seam: the existing
/// <c>SqlParser(…, IStorage storage, …)</c> constructor is kept unchanged and internally
/// wraps <c>storage</c> in a <see cref="DirectoryTableFactory"/>. Callers that want
/// single-file parity can pass a <see cref="SingleFileTableFactory"/> instead via the new
/// <c>SqlParser(…, ITableFactory tableFactory, …)</c> constructor.
/// </remarks>
public interface ITableFactory
{
    /// <summary>
    /// Creates a new, empty <see cref="ITable"/> backed by this factory's storage.
    /// The caller is responsible for setting all remaining schema properties on the
    /// returned instance before adding it to the table dictionary.
    /// </summary>
    /// <param name="tableName">The logical table name (used as the storage block key for single-file mode).</param>
    /// <param name="isReadOnly">Whether the table should be opened in read-only mode.</param>
    /// <param name="config">Optional database configuration used for optimisation hints.</param>
    /// <returns>A fresh <see cref="ITable"/> instance ready for schema population.</returns>
    ITable CreateTable(string tableName, bool isReadOnly, DatabaseConfig? config);
}
