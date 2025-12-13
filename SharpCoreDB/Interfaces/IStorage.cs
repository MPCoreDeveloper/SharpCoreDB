// <copyright file="IStorage.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Interfaces;

/// <summary>
/// Interface for encrypted file storage operations, supporting memory-mapped files for performance.
/// </summary>
public interface IStorage
{
    /// <summary>
    /// Writes data to an encrypted file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="data">The data to write.</param>
    void Write(string path, string data);

    /// <summary>
    /// Reads data from an encrypted file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The read data, or null if file does not exist.</returns>
    string? Read(string path);

    /// <summary>
    /// Reads data using memory-mapped file for large files.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The read data.</returns>
    string? ReadMemoryMapped(string path);

    /// <summary>
    /// Writes binary data to an encrypted file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="data">The data to write.</param>
    void WriteBytes(string path, byte[] data);

    /// <summary>
    /// Reads binary data from an encrypted file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The read data, or null if file does not exist.</returns>
    byte[]? ReadBytes(string path);

    /// <summary>
    /// Reads binary data from an encrypted file with optional encryption bypass.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="noEncrypt">If true, bypasses encryption for this operation.</param>
    /// <returns>The read data, or null if file does not exist.</returns>
    byte[]? ReadBytes(string path, bool noEncrypt);

    /// <summary>
    /// Appends binary data to a file (used for high-performance inserts).
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="data">The data to append.</param>
    /// <returns>The offset where the data was appended.</returns>
    long AppendBytes(string path, byte[] data);

    /// <summary>
    /// Appends multiple binary data blocks to a file in a single batch operation (used for batch inserts).
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="dataBlocks">The list of data blocks to append.</param>
    /// <returns>Array of offsets where each data block was appended.</returns>
    long[] AppendBytesMultiple(string path, List<byte[]> dataBlocks);

    /// <summary>
    /// Reads binary data from a file starting from the specified offset.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="offset">The offset to start reading from.</param>
    /// <returns>The read data from offset to end, or null if file does not exist.</returns>
    byte[]? ReadBytesFrom(string path, long offset);

    /// <summary>
    /// Reads binary data from a file starting at the specified position with a maximum length.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="position">The position to start reading from.</param>
    /// <param name="maxLength">The maximum number of bytes to read.</param>
    /// <returns>The read data, or null if file does not exist or position is invalid.</returns>
    byte[]? ReadBytesAt(string path, long position, int maxLength);

    /// <summary>
    /// Reads binary data from a file starting at the specified position with a maximum length, with optional encryption bypass.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="position">The position to start reading from.</param>
    /// <param name="maxLength">The maximum number of bytes to read.</param>
    /// <param name="noEncrypt">If true, bypasses encryption for this operation.</param>
    /// <returns>The read data, or null if file does not exist or position is invalid.</returns>
    byte[]? ReadBytesAt(string path, long position, int maxLength, bool noEncrypt);
}
