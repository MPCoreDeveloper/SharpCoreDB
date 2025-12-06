// <copyright file="IWAL.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
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
