// <copyright file="Table.FixedWidthMigration.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.DataStructures;

using SharpCoreDB.Storage.Hybrid;

/// <summary>
/// B5: 1.x → 2.0 record-format migration. Converts a legacy table (variable-length records) to the
/// fixed-width record layout (out-of-line overflow arena): current rows are re-read through the
/// legacy codec, re-serialized as fixed-width records (variable values move into a fresh overflow
/// arena), and the primary-key / hash indexes are rebuilt on the new record positions.
/// </summary>
public partial class Table
{
    /// <summary>
    /// Migrates this table from the legacy variable-length record format to the fixed-width record
    /// layout. Returns the number of rows migrated (0 when the table is already fixed-width).
    /// Requires a writable, columnar (append-only) table.
    /// </summary>
    public int MigrateToFixedWidth()
    {
        if (isReadOnly)
        {
            throw new InvalidOperationException("Cannot migrate a read-only table to the fixed-width record layout.");
        }

        if (StorageMode != StorageMode.Columnar)
        {
            throw new NotSupportedException(
                "The fixed-width record layout supports columnar/append-only tables only. " +
                "Convert the table to Columnar storage first (StorageMigrator.MigrateToColumnar).");
        }

        rwLock.EnterWriteLock();
        try
        {
            if (_fixedWidthRecords)
            {
                return 0; // already in the target format
            }

            // B5 safety net: a table created with the fixed-width flag BEFORE the record format was
            // persisted in metadata (B1–B4) is unmarked but already stores fixed-width records.
            // Re-reading it as legacy would corrupt it, so adopt the format when the on-disk records
            // provably match the fixed-width layout (constant length + variable slots resolve in the
            // arena). Legacy records with fixed-size-only columns are byte-identical to fixed-width,
            // so adopting is also correct for them.
            if (RecordsMatchFixedWidthLayout())
            {
                _fixedWidthRecords = true;
                return 0;
            }

            // 1. Rebuild the PK index with the LEGACY codec so Select() filters stale versions
            //    correctly (the index may be empty right after metadata load).
            if (PrimaryKeyIndex >= 0)
            {
                RebuildPrimaryKeyIndexFromDisk();
            }

            // 2. Read the current rows through the legacy (variable-length) codec. This runs under
            //    the write lock — the recursive rwLock allows the nested read-side Select.
            var rows = Select();

            // 3. Switch the serializer to the fixed-width codec and start with a fresh arena.
            var arenaPath = System.IO.Path.ChangeExtension(DataFile, ".ovf");
            if (File.Exists(arenaPath))
            {
                File.Delete(arenaPath);
            }

            _overflowArena = null;
            _fixedWidthLayout = null;
            _fixedWidthRecords = true;

            // 4. Re-serialize every row as a fixed-width record (variable values → fresh arena).
            var records = new List<byte[]>(rows.Count);
            foreach (var row in rows)
            {
                records.Add(SerializeRowFixedWidth(row));
            }

            // 5. Write the new records to a temp file and swap it in atomically (the same pattern
            //    as AppendOnlyEngine.CompactTable, so encryption handling is identical).
            var tempPath = DataFile + ".fwmig.tmp";
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                if (records.Count > 0)
                {
                    storage.AppendBytesMultiple(tempPath, records);
                }
                else
                {
                    File.WriteAllBytes(tempPath, Array.Empty<byte>());
                }

                File.Delete(DataFile);
                File.Move(tempPath, DataFile);
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
                }

                throw;
            }

            // 6. Rebuild the indexes against the new fixed-width records (DeserializeRow dispatches
            //    to the fixed-width codec now) and fix the cached row count.
            RebuildPrimaryKeyIndex();
            foreach (var col in loadedIndexes.ToList())
            {
                RebuildHashIndex(col);
            }

            Interlocked.Exchange(ref _cachedRowCount, rows.Count);

            return rows.Count;
        }
        finally
        {
            rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Probes the on-disk records to determine whether they already use the fixed-width layout.
    /// Returns true when every record has exactly <see cref="FixedWidthRecordLayout.FixedSize"/>
    /// bytes AND every non-NULL variable slot resolves to a block in the overflow arena. Legacy
    /// records (variable-length strings/blobs) fail the length or the arena-resolution check, so
    /// they never match; fixed-size-only legacy records are byte-identical and safely adopt.
    /// </summary>
    private bool RecordsMatchFixedWidthLayout()
    {
        var layout = GetFixedWidthLayout();
        var arena = GetOverflowArena();
        var engine = GetOrCreateStorageEngine();
        bool any = false;

        foreach (var (_, data) in engine.GetAllRecords(Name))
        {
            any = true;
            if (data is not { Length: var len } || len != layout.FixedSize)
            {
                return false;
            }

            for (int i = 0; i < layout.ColumnCount; i++)
            {
                if (!layout.IsVariable[i])
                {
                    continue;
                }

                var slot = layout.Offsets[i];
                if (slot + 5 > data.Length)
                {
                    return false;
                }

                if (data[slot] == 0)
                {
                    continue; // NULL slot — valid in either format
                }

                // The slot must be an arena offset, not a legacy string-length prefix.
                var offset = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(slot + 1, 4));
                if (arena.Read(offset) is null)
                {
                    return false;
                }
            }
        }

        return any; // true only when at least one record exists and all records match
    }
}
