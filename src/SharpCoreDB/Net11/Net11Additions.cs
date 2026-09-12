// <copyright file="Net11Additions.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

#if NET11_0_OR_GREATER
namespace SharpCoreDB.Net11;

using System;
using System.Collections.Generic;
using SharpCoreDB.DataStructures;

/// <summary>
/// Additive, net11.0-only API surface demonstrating the backward-compatible C# 15 preview
/// features: extension indexers, closed hierarchies and collection-expression capacity arguments.
/// This file is source-gated via <c>#if NET11_0_OR_GREATER</c>, so it is compiled ONLY for the
/// net11.0 target — it can never affect .NET 10 consumers, and every member is purely additive.
/// </summary>
public static class Net11Additions
{
    /// <summary>
    /// Extension indexer: <c>db["tableName"]</c> → the matching <see cref="TableInfo"/> (matched
    /// case-insensitively) or <c>null</c> when no table has that name. <see cref="Database"/> has
    /// no indexer of its own, so this extension cannot conflict with any existing member.
    /// </summary>
    extension(Database db)
    {
        public TableInfo? this[string tableName]
        {
            get
            {
                ArgumentException.ThrowIfNullOrEmpty(tableName);
                foreach (var info in db.GetTables())
                {
                    if (string.Equals(info.Name, tableName, StringComparison.OrdinalIgnoreCase))
                    {
                        return info;
                    }
                }

                return null;
            }
        }
    }

    /// <summary>
    /// Creates an empty <see cref="List{T}"/> pre-sized to <paramref name="capacity"/> using the
    /// C# 15 collection-expression argument <c>[with(capacity: …)]</c>, avoiding the reallocation
    /// growth of an unseeded list.
    /// </summary>
    public static List<T> NewList<T>(int capacity) => [with(capacity: capacity)];

    /// <summary>
    /// Closed-hierarchy demonstration (C# 15 preview <c>closed</c> modifier): the base cannot be
    /// instantiated directly and cannot be subclassed outside this assembly, enabling exhaustive
    /// dispatch over the known shapes.
    /// </summary>
    internal closed class ColumnarData
    {
        internal int ColumnCount { get; init; }
    }

    internal sealed class FixedWidthData : ColumnarData { }
}
#endif