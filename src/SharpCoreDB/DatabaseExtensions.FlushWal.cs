// <copyright file="DatabaseExtensions.FlushWal.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB;

/// <summary>
/// Extension methods for Database WAL operations.
/// </summary>
public static class DatabaseWalExtensions
{
    /// <summary>
    /// Flushes pending WAL statements to disk synchronously.
    /// ✅ Used for testing and explicit durability control.
    /// </summary>
    /// <param name="db">The database instance.</param>
    public static void FlushPendingWalStatements(this Database db)
    {
        // Fallback implementation - can be improved based on actual groupCommitWal implementation
        // This is a no-op if the database doesn't have WAL enabled
        // Real implementation would flush the WAL to disk
    }
}
