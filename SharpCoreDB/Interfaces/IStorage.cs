// <copyright file="IStorage.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
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
}
