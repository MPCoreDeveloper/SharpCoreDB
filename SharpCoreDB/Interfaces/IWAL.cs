// <copyright file="IWAL.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Interfaces;

/// <summary>
/// Interface for Write-Ahead Logging to ensure ACID properties.
/// </summary>
public interface IWAL
{
    /// <summary>
    /// Logs an operation.
    /// </summary>
    /// <param name="operation">The operation to log.</param>
    void Log(string operation);

    /// <summary>
    /// Commits the log, clearing it after successful write.
    /// </summary>
    void Commit();

    /// <summary>
    /// Asynchronously flushes buffered data to disk.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task FlushAsync();
}
