// <copyright file="IStorageEngine.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Interfaces;

/// <summary>
/// Interface for storage engines that handle low-level data persistence.
/// Provides abstraction over different storage strategies (append-only, page-based, columnar).
/// </summary>
public interface IStorageEngine : IDisposable
{
    /// <summary>
    /// Gets the storage engine type.
    /// </summary>
    StorageEngineType EngineType { get; }

    /// <summary>
    /// Inserts a new record and returns the storage location reference.
    /// </summary>
    /// <param name="tableName">Name of the table.</param>
    /// <param name="data">Record data to insert.</param>
    /// <returns>Storage reference that can be used for retrieval.</returns>
    long Insert(string tableName, byte[] data);

    /// <summary>
    /// Inserts multiple records in a batch operation.
    /// </summary>
    /// <param name="tableName">Name of the table.</param>
    /// <param name="dataBlocks">List of records to insert.</param>
    /// <returns>Array of storage references.</returns>
    long[] InsertBatch(string tableName, List<byte[]> dataBlocks);

    /// <summary>
    /// Updates an existing record at the specified storage reference.
    /// </summary>
    /// <param name="tableName">Name of the table.</param>
    /// <param name="storageReference">Storage location of the record.</param>
    /// <param name="newData">New record data.</param>
    void Update(string tableName, long storageReference, byte[] newData);

    /// <summary>
    /// Deletes a record at the specified storage reference.
    /// </summary>
    /// <param name="tableName">Name of the table.</param>
    /// <param name="storageReference">Storage location of the record.</param>
    void Delete(string tableName, long storageReference);

    /// <summary>
    /// Reads a record from the specified storage reference.
    /// </summary>
    /// <param name="tableName">Name of the table.</param>
    /// <param name="storageReference">Storage location of the record.</param>
    /// <returns>The record data, or null if not found or deleted.</returns>
    byte[]? Read(string tableName, long storageReference);

    /// <summary>
    /// Begins a transaction for batched operations.
    /// </summary>
    void BeginTransaction();

    /// <summary>
    /// Commits the current transaction.
    /// </summary>
    /// <returns>Task representing the commit operation.</returns>
    Task CommitAsync();

    /// <summary>
    /// Rolls back the current transaction.
    /// </summary>
    void Rollback();

    /// <summary>
    /// Gets whether currently in a transaction.
    /// </summary>
    bool IsInTransaction { get; }

    /// <summary>
    /// Flushes any buffered data to disk without committing transaction.
    /// </summary>
    void Flush();

    /// <summary>
    /// Gets performance metrics for this storage engine.
    /// </summary>
    /// <returns>Performance metrics.</returns>
    StorageEngineMetrics GetMetrics();
}

/// <summary>
/// Types of storage engines available.
/// </summary>
public enum StorageEngineType
{
    /// <summary>Append-only storage with sequential writes.</summary>
    AppendOnly,

    /// <summary>Page-based storage with in-place updates.</summary>
    PageBased,

    /// <summary>Columnar storage for analytical workloads.</summary>
    Columnar,

    /// <summary>Hybrid storage combining multiple strategies.</summary>
    Hybrid
}

/// <summary>
/// Performance metrics for a storage engine.
/// </summary>
public record StorageEngineMetrics
{
    /// <summary>Gets or initializes total number of inserts.</summary>
    public long TotalInserts { get; init; }

    /// <summary>Gets or initializes total number of updates.</summary>
    public long TotalUpdates { get; init; }

    /// <summary>Gets or initializes total number of deletes.</summary>
    public long TotalDeletes { get; init; }

    /// <summary>Gets or initializes total number of reads.</summary>
    public long TotalReads { get; init; }

    /// <summary>Gets or initializes total bytes written.</summary>
    public long BytesWritten { get; init; }

    /// <summary>Gets or initializes total bytes read.</summary>
    public long BytesRead { get; init; }

    /// <summary>Gets or initializes average insert time in microseconds.</summary>
    public double AvgInsertTimeMicros { get; init; }

    /// <summary>Gets or initializes average update time in microseconds.</summary>
    public double AvgUpdateTimeMicros { get; init; }

    /// <summary>Gets or initializes average delete time in microseconds.</summary>
    public double AvgDeleteTimeMicros { get; init; }

    /// <summary>Gets or initializes average read time in microseconds.</summary>
    public double AvgReadTimeMicros { get; init; }

    /// <summary>Gets or initializes engine-specific metrics.</summary>
    public Dictionary<string, object> CustomMetrics { get; init; } = new();
}
