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
    /// Begins a transaction for batched write operations.
    /// All writes after this call are buffered and must be committed with Commit() or rolled back with Rollback().
    /// </summary>
    void BeginTransaction();

    /// <summary>
    /// Commits the current transaction, writing all buffered data to disk.
    /// Returns a task that completes when writes are durable.
    /// </summary>
    Task CommitAsync();

    /// <summary>
    /// Commits the current transaction synchronously without thread-pool overhead.
    /// Use in hot paths where <see cref="CommitAsync"/> would cause an unnecessary thread switch.
    /// Default implementation falls back to <see cref="CommitAsync"/> for compatibility.
    /// </summary>
    void CommitSync() => CommitAsync().GetAwaiter().GetResult();

    /// <summary>
    /// Rolls back the current transaction, discarding all buffered writes.
    /// </summary>
    void Rollback();

    /// <summary>
    /// Flushes transaction buffer to disk without committing the transaction.
    /// Used for intermediate flushes during bulk insert operations to prevent excessive memory buildup.
    /// </summary>
    void FlushTransactionBuffer();

    /// <summary>
    /// Checks if currently inside a transaction.
    /// </summary>
    bool IsInTransaction { get; }

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
    /// Overwrites a length-prefixed record in place at <paramref name="offset"/> (in-place UPDATE).
    /// Returns true only when the new (encrypted) record fits the existing slot — i.e. the stored
    /// length is unchanged, so every following record stays at a valid offset. When the lengths
    /// differ the caller must fall back to <see cref="AppendBytes"/>. Not available inside a
    /// transaction (buffered appends + rollback are append-only by design).
    /// </summary>
    bool OverwriteRecordAt(string path, long offset, byte[] data);

    /// <summary>
    /// Like <see cref="OverwriteRecordAt"/> for the case where the caller guarantees
    /// <paramref name="data"/> has the same byte length as the stored record payload (an in-place
    /// field patch built from the existing row). Implementations may skip the length-prefix
    /// read/verification; the default routes to <see cref="OverwriteRecordAt"/>.
    /// </summary>
    bool OverwriteRecordAtSameLength(string path, long offset, byte[] data) =>
        OverwriteRecordAt(path, offset, data);

    /// <summary>
    /// True when an in-place overwrite is currently buffered for <paramref name="path"/> (the
    /// transaction write-behind buffer overlays reads of those offsets). Callers that bypass the
    /// per-record read path must check this first so they never read stale disk bytes.
    /// </summary>
    bool HasBufferedOverwrite(string path) => false;

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

    /// <summary>
    /// Reads a raw contiguous byte range starting at <paramref name="offset"/> using the storage
    /// layer's cached file handle (no per-call handle open). Used by the fixed-width contiguous
    /// UPDATE fast path, which only engages on plaintext files. Implementations that cannot serve a
    /// raw range (encrypted layouts, mocks) return null so the caller falls back to per-record reads.
    /// </summary>
    byte[]? ReadBytesRange(string path, long offset, int length) => null;

    /// <summary>
    /// True when the file at <paramref name="path"/> stores per-record encrypted (ciphertext)
    /// payloads (it carries the encrypted-table magic header). Raw range reads must never be used on
    /// such files; the default returns false (plaintext / legacy layouts).
    /// </summary>
    bool AreRecordsEncrypted(string path) => false;

    /// <summary>
    /// Marks the record whose 4-byte length prefix sits at <paramref name="offset"/> as deleted by
    /// replacing the prefix with the NEGATIVE slot size (4-byte prefix + payload). Every record
    /// enumerator treats a negative prefix as a deleted record and skips |value| bytes, so the
    /// delete survives a reopen without rewriting the file. The default returns false (unsupported
    /// layout / mock storage).
    /// </summary>
    bool TombstoneRecord(string path, long offset) => false;

    /// <summary>
    /// Enumerates every record in a table data file, yielding the literal file offset of the
    /// 4-byte length prefix (the offset returned by <see cref="AppendBytes"/>) together with the
    /// decrypted (or plaintext) record payload. Format-agnostic: transparently handles both legacy
    /// plaintext files and per-record AES-256-GCM encrypted files (those carrying the magic header
    /// <c>SharpCoreDB.Constants.PersistenceConstants.EncryptedTableMagic</c>).
    /// Used by full-table scans, primary-key index rebuilds and compaction so B-tree positions
    /// always match point-lookup offsets. Default implementation parses plaintext only for
    /// compatibility with alternative <see cref="IStorage"/> implementations.
    /// </summary>
    /// <param name="path">The table data file path.</param>
    /// <returns>Sequence of (recordOffset, recordData) tuples in file order.</returns>
    IEnumerable<(long RecordOffset, byte[] Data)> ReadAllRecords(string path)
    {
        if (!File.Exists(path))
        {
            yield break;
        }

        var data = ReadBytes(path, noEncrypt: true);
        if (data is null || data.Length == 0)
        {
            yield break;
        }

        const int MaxRecordSizeLocal = 1_000_000_000;
        long position = 0;
        while (position + 4 <= data.Length)
        {
            int length = BitConverter.ToInt32(data, (int)position);
            if (length <= 0 || length > MaxRecordSizeLocal || position + 4 + length > data.Length)
            {
                yield break;
            }

            var record = new byte[length];
            Array.Copy(data, position + 4, record, 0, length);
            yield return (position, record);
            position += 4 + length;
        }
    }
}