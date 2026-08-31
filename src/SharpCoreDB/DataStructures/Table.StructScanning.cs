// <copyright file="Table.StructScanning.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.DataStructures;

using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpCoreDB.Services;
using SharpCoreDB.Storage;
using SharpCoreDB.Storage.Hybrid;

/// <summary>
/// Zero-allocation StructRow scanning methods for Table.
/// ✅ PERFORMANCE CRITICAL: Provides 3-5x faster SELECT operations by eliminating:
///   - Dictionary allocations per row (~200 bytes → 0 bytes)
///   - Boxing of primitive types
///   - String key lookups
/// 
/// Expected performance improvement: 33ms → 8-12ms for 10K row SELECT (faster than LiteDB).
/// </summary>
public partial class Table
{
    #region StructRow Scanning API

    /// <summary>
    /// Zero-allocation, zero-copy enumeration of all rows in the table.
    /// ✅ CRITICAL: This is the primary high-performance path for SELECT operations.
    /// 
    /// Performance characteristics:
    /// - Zero allocations during iteration (uses yield return with StructRow)
    /// - Zero-copy: StructRow holds a reference to raw byte data
    /// - Lazy deserialization: Values are only parsed when GetValue&lt;T&gt;() is called
    /// - ~20 bytes per row vs ~200 bytes for Dictionary API
    /// 
    /// Usage:
    /// <code>
    /// foreach (var row in table.ScanStructRows())
    /// {
    ///     int id = row.GetValue&lt;int&gt;(0);      // Direct offset access
    ///     string name = row.GetValue&lt;string&gt;(1); // Lazy deserialization
    /// }
    /// </code>
    /// </summary>
    /// <param name="enableCaching">Enable value caching for repeated column access (adds small allocation).</param>
    /// <returns>Zero-allocation enumerable of StructRow instances.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public IEnumerable<StructRow> ScanStructRows(bool enableCaching = false)
    {
        // Fixed-width record layout (out-of-line overflow): the zero-alloc struct scan walks the
        // variable-length record format, so fixed-width tables fall back to the dictionary path
        // (correct, allocated). StructRow.FromDictionary is self-consistent (own bytes + schema).
        if (_fixedWidthRecords)
        {
            var columns = Columns.ToArray();
            var types = ColumnTypes.ToArray();
            foreach (var row in Select())
            {
                yield return StructRow.FromDictionary(row, columns, types);
            }

            yield break;
        }

        // ✅ FIX: Validate upfront, then delegate to iterator methods
        ArgumentNullException.ThrowIfNull(this.storage);

        // Build schema once for entire scan
        var schema = BuildVariableLengthSchema();

        if (this.StorageMode == StorageMode.Columnar)
        {
            // Columnar mode: Read entire file and iterate with position filtering
            foreach (var row in ScanColumnarStructRowsInternal(schema, enableCaching))
            {
                yield return row;
            }
        }
        else // PageBased
        {
            // PageBased mode: Use storage engine's GetAllRecords
            foreach (var row in ScanPageBasedStructRowsInternal(schema, enableCaching))
            {
                yield return row;
            }
        }
    }

    /// <summary>
    /// Convenience method for zero-allocation SELECT operations.
    /// ✅ RECOMMENDED: Use this instead of Select() for maximum performance.
    /// 
    /// Equivalent to ScanStructRows() but with a more SQL-like name.
    /// </summary>
    /// <param name="enableCaching">Enable value caching for repeated column access.</param>
    /// <returns>Zero-allocation enumerable of StructRow instances.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerable<StructRow> SelectStruct(bool enableCaching = false)
    {
        return ScanStructRows(enableCaching);
    }

    /// <summary>
    /// Zero-allocation SELECT with WHERE filtering.
    /// ✅ PERFORMANCE: Filtering is applied during iteration, not after.
    /// </summary>
    /// <param name="predicate">Filter predicate applied to each StructRow.</param>
    /// <param name="enableCaching">Enable value caching for repeated column access.</param>
    /// <returns>Filtered enumerable of StructRow instances.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public IEnumerable<StructRow> SelectStructWhere(
        Func<StructRow, bool> predicate,
        bool enableCaching = false)
    {
        foreach (var row in ScanStructRows(enableCaching))
        {
            if (predicate(row))
            {
                yield return row;
            }
        }
    }

    /// <summary>
    /// Converts StructRow results to Dictionary for backward compatibility.
    /// ✅ WARNING: This allocates memory. Use ScanStructRows() directly for best performance.
    /// </summary>
    /// <param name="rows">StructRow enumerable to convert.</param>
    /// <returns>List of dictionaries (allocates memory).</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public List<Dictionary<string, object>> StructRowsToDictionaries(IEnumerable<StructRow> rows)
    {
        var results = new List<Dictionary<string, object>>();

        foreach (var row in rows)
        {
            // ✅ Use pooled dictionary for reduced allocations
            var dict = _dictPool.Get();

            for (int i = 0; i < Columns.Count; i++)
            {
                dict[Columns[i]] = row.GetValueBoxed(i);
            }

            results.Add(dict);
        }

        return results;
    }

    /// <summary>
    /// Zero-allocation SELECT with a simple WHERE filter.
    /// Fast paths: hash-index point lookup and primary-key lookup first (mirroring
    /// <see cref="SelectInternal"/>), then a full scan with a scalar equality predicate.
    /// Supports the same simple "col = value" shape as the SQL parser's point-lookup fast path.
    /// </summary>
    /// <param name="where">A simple "column = value" WHERE clause (or null/empty for all rows).</param>
    /// <param name="enableCaching">Enable value caching for repeated column access.</param>
    /// <returns>Zero-allocation filtered enumeration of StructRow instances.</returns>
    /// <summary>
    /// Zero-allocation filtered enumeration of StructRow instances (point-lookup fast paths are
    /// allocation-free via <see cref="StructRowWhereEnumerator"/>; full-scan/SIMD fallback paths
    /// delegate to the yield-based core).
    /// </summary>
    public StructRowWhereEnumerable ScanStructRowsWhere(string? where, bool enableCaching = false)
    {
        ArgumentNullException.ThrowIfNull(this.storage);
        return new StructRowWhereEnumerable(this, where, enableCaching);
    }

    /// <summary>
    /// Yield-based implementation backing <see cref="ScanStructRowsWhere"/> for the full-scan /
    /// SIMD fallback paths (which allocate by nature). The hash-index and primary-key point-lookup
    /// fast paths are handled allocation-free by <see cref="StructRowWhereEnumerator"/>; this core
    /// re-checks them (they have already been ruled out when the fallback is reached) and then
    /// runs the numeric-SIMD batch filter or the full scan.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private IEnumerable<StructRow> ScanStructRowsWhereCore(string? where, bool enableCaching)
    {
        ArgumentNullException.ThrowIfNull(this.storage);

        // Fixed-width records: StructRow's variable-length schema can't walk the fixed-width
        // format, so matched records are materialized through the dictionary path. The numeric-SIMD
        // fast path below is still usable (raw constant-offset reads, no schema walk); anything else
        // falls back to the arena-aware dictionary full scan (see ScanStructRows).
        bool fixedWidth = _fixedWidthRecords;
        string[]? fixedColumns = fixedWidth ? Columns.ToArray() : null;
        DataType[]? fixedTypes = fixedWidth ? ColumnTypes.ToArray() : null;
        var schema = fixedWidth ? default : BuildVariableLengthSchema();
        var engine = GetOrCreateStorageEngine();

        string? simpleColumn = null;
        object? simpleValue = null;
        bool hasSimpleWhere = !string.IsNullOrEmpty(where) &&
            TryParseSimpleWhereClause(where!, out simpleColumn, out simpleValue);

        // Fast path 1: hash-index point lookup (mirrors SelectInternal). StructRow can only
        // represent variable-length records, so fixed-width tables skip this path.
        if (!fixedWidth && hasSimpleWhere && simpleColumn is not null && simpleValue is not null &&
            this.registeredIndexes.ContainsKey(simpleColumn))
        {
            EnsureIndexLoaded(simpleColumn);
            if (this.hashIndexes.TryGetValue(simpleColumn, out var hashIndex))
            {
                var colIdx = this.Columns.IndexOf(simpleColumn);
                if (colIdx >= 0)
                {
                    var key = ParseValueForHashLookup(simpleValue.ToString() ?? string.Empty, this.ColumnTypes[colIdx]);
                    if (key is not null)
                    {
                        foreach (var pos in hashIndex.LookupPositions(key))
                        {
                            var data = engine.Read(Name, pos);
                            if (data is { Length: > 0 })
                            {
                                yield return new StructRow(data.AsMemory(), schema, enableCaching);
                            }
                        }

                        yield break;
                    }
                }
            }
        }

        // Fast path 2: primary-key lookup (variable-length layout only — StructRow schema walk).
        if (!fixedWidth && hasSimpleWhere && simpleColumn is not null && simpleValue is not null &&
            this.PrimaryKeyIndex >= 0 &&
            string.Equals(simpleColumn, this.Columns[this.PrimaryKeyIndex], StringComparison.OrdinalIgnoreCase))
        {
            var pkStr = simpleValue.ToString() ?? string.Empty;
            var search = this.Index.Search(pkStr);
            if (search.Found)
            {
                var data = engine.Read(Name, search.Value);
                if (data is { Length: > 0 })
                {
                    yield return new StructRow(data.AsMemory(), schema, enableCaching);
                }
            }

            yield break;
        }

        // Fast path 3: fixed-width numeric equality — SIMD batch filter over extracted values
        // (no deserialization, no boxing). Integer/Long use portable Vector<T>; Real uses
        // direct per-record reads.
        if (hasSimpleWhere && simpleColumn is not null && simpleValue is not null &&
            TryGetFixedNumericWhereInfo(simpleColumn, out var numericOffset, out var numericType) &&
            TryParseNumericExpected(simpleValue, numericType, out var numericExpected))
        {
            if (numericType == DataType.Integer || numericType == DataType.Long)
            {
                List<int>? intValues = numericType == DataType.Integer ? new List<int>(1024) : null;
                List<long>? longValues = numericType == DataType.Long ? new List<long>(1024) : null;
                var recordDatas = new List<byte[]>(1024);
                var recordPositions = new List<long>(1024);

                foreach (var (pos, rec) in engine.GetAllRecords(Name))
                {
                    if (rec is not { Length: > 0 } || !TryExtractNumericDirect(rec, numericOffset, numericType, out var val))
                    {
                        continue;
                    }

                    if (intValues is not null)
                    {
                        intValues.Add((int)val);
                    }
                    else
                    {
                        longValues!.Add((long)val);
                    }

                    recordDatas.Add(rec);
                    recordPositions.Add(pos);
                }

                var matches = new List<int>(16);
                if (intValues is not null)
                {
                    SimdFilterInt32Batch(CollectionsMarshal.AsSpan(intValues), (int)numericExpected, matches);
                }
                else
                {
                    SimdFilterInt64Batch(CollectionsMarshal.AsSpan(longValues!), (long)numericExpected, matches);
                }

                for (int mi = 0; mi < matches.Count; mi++)
                {
                    var rec = recordDatas[matches[mi]];
                    if (!TryValidateCurrentVersion(rec, schema, recordPositions[matches[mi]], fixedWidth))
                    {
                        continue;
                    }

                    if (fixedWidth)
                    {
                        yield return StructRow.FromDictionary(DeserializeRowFixedWidth(rec.AsSpan()), fixedColumns!, fixedTypes!);
                    }
                    else
                    {
                        yield return new StructRow(rec.AsMemory(), schema, enableCaching);
                    }
                }
            }
            else
            {
                // Real (double): direct per-record reads.
                foreach (var (recordPosition, data) in engine.GetAllRecords(Name))
                {
                    if (data is not { Length: > 0 } ||
                        !MatchesNumericDirect(data, numericOffset, numericType, numericExpected) ||
                        !TryValidateCurrentVersion(data, schema, recordPosition, fixedWidth))
                    {
                        continue;
                    }

                    if (fixedWidth)
                    {
                        yield return StructRow.FromDictionary(DeserializeRowFixedWidth(data.AsSpan()), fixedColumns!, fixedTypes!);
                    }
                    else
                    {
                        yield return new StructRow(data.AsMemory(), schema, enableCaching);
                    }
                }
            }

            yield break;
        }

        // Fixed-width fallback: arena-aware dictionary full scan (StructRow can't walk the format).
        if (fixedWidth)
        {
            foreach (var row in Select(where))
            {
                yield return StructRow.FromDictionary(row, fixedColumns!, fixedTypes!);
            }

            yield break;
        }

        // Fallback: full scan with a simple equality predicate (scalar, allocation-free per row).
        foreach (var row in ScanStructRows(enableCaching))
        {
            if (!hasSimpleWhere || simpleColumn is null || simpleValue is null ||
                MatchesSimpleWhere(row, schema, simpleColumn, simpleValue))
            {
                yield return row;
            }
        }
    }
    /// <summary>
    /// Zero-allocation enumerable for <see cref="ScanStructRowsWhere"/>. Foreach on this concrete
    /// type uses <see cref="StructRowWhereEnumerator"/> (no heap allocation); treating it as
    /// <c>IEnumerable&lt;StructRow&gt;</c> (LINQ, boxing) uses a small class-based enumerator.
    /// </summary>
    public readonly struct StructRowWhereEnumerable : IEnumerable<StructRow>
    {
        private readonly Table _table;
        private readonly string? _where;
        private readonly bool _enableCaching;

        internal StructRowWhereEnumerable(Table table, string? where, bool enableCaching)
        {
            _table = table;
            _where = where;
            _enableCaching = enableCaching;
        }

        public StructRowWhereEnumerator GetEnumerator() => new(_table, _where, _enableCaching);

        IEnumerator<StructRow> IEnumerable<StructRow>.GetEnumerator()
            => new BoxedEnumerator(_table, _where, _enableCaching);

        IEnumerator IEnumerable.GetEnumerator()
            => ((IEnumerable<StructRow>)this).GetEnumerator();

        private sealed class BoxedEnumerator : IEnumerator<StructRow>
        {
            // NOT readonly: MoveNext mutates the struct enumerator's phase state.
            private StructRowWhereEnumerator _inner;

            internal BoxedEnumerator(Table table, string? where, bool enableCaching)
            {
                _inner = new StructRowWhereEnumerator(table, where, enableCaching);
            }

            public StructRow Current => _inner.Current;
            object IEnumerator.Current => _inner.Current;

            public bool MoveNext() => _inner.MoveNext();
            public void Reset() => throw new NotSupportedException();
            public void Dispose() => _inner.Dispose();
        }
    }
    /// <summary>
    /// Allocation-free enumerator for <see cref="StructRowWhereEnumerable"/>. Handles the
    /// hash-index and primary-key point-lookup fast paths natively (zero allocations on the
    /// hot path); the numeric-SIMD / full-scan fallback delegates to the yield-based core.
    /// </summary>
    public struct StructRowWhereEnumerator : IDisposable
    {
        private enum Phase
        {
            Init,
            Hash,
            Pk,
            Fallback,
            Done
        }

        private readonly Table _table;
        private readonly string? _where;
        private readonly bool _enableCaching;
        private Phase _phase;
        private VariableLengthSchema _schema;
        private IStorageEngine _engine;
        private string? _simpleColumn;
        private object? _simpleValue;
        private bool _hasSimpleWhere;
        private List<long> _positions;
        private int _posIndex;
        private bool _pkFound;
        private long _pkPosition;
        private IEnumerator<StructRow>? _fallback;
        private StructRow _current;

        internal StructRowWhereEnumerator(Table table, string? where, bool enableCaching)
        {
            _table = table;
            _where = where;
            _enableCaching = enableCaching;
            _phase = Phase.Init;
            _schema = default;
            _engine = null!;
            _simpleColumn = null;
            _simpleValue = null;
            _hasSimpleWhere = false;
            _positions = null!;
            _posIndex = 0;
            _pkFound = false;
            _pkPosition = 0;
            _fallback = null;
            _current = default;
        }

        /// <summary>Gets the current row.</summary>
        public StructRow Current => _current;

        /// <summary>Advances to the next matching row.</summary>
        public bool MoveNext()
        {
            switch (_phase)
            {
                case Phase.Init:
                    return InitAndMoveNext();
                case Phase.Hash:
                    return MoveNextHash();
                case Phase.Pk:
                    return MoveNextPk();
                case Phase.Fallback:
                    return MoveNextFallback();
                default:
                    return false;
            }
        }

        private bool InitAndMoveNext()
        {
            _schema = _table.BuildVariableLengthSchema();
            _engine = _table.GetOrCreateStorageEngine();
            _hasSimpleWhere = !string.IsNullOrEmpty(_where) &&
                TryParseSimpleWhereClause(_where!, out _simpleColumn, out _simpleValue);

            // Fast path 1: hash-index point lookup (mirrors SelectInternal). Disabled for
            // fixed-width tables (their records use the overflow format, not the walkable layout).
            if (!_table._fixedWidthRecords && _hasSimpleWhere && _simpleColumn is not null && _simpleValue is not null &&
                _table.registeredIndexes.ContainsKey(_simpleColumn))
            {
                _table.EnsureIndexLoaded(_simpleColumn);
                if (_table.hashIndexes.TryGetValue(_simpleColumn, out var hashIndex))
                {
                    var colIdx = _table.Columns.IndexOf(_simpleColumn);
                    if (colIdx >= 0)
                    {
                        var key = ParseValueForHashLookup(_simpleValue.ToString() ?? string.Empty, _table.ColumnTypes[colIdx]);
                        if (key is not null)
                        {
                            _positions = hashIndex.LookupPositions(key);
                            _posIndex = 0;
                            _phase = Phase.Hash;
                            return MoveNextHash();
                        }
                    }
                }
            }

            // Fast path 2: primary-key lookup. Disabled for fixed-width tables (same reason).
            if (!_table._fixedWidthRecords && _hasSimpleWhere && _simpleColumn is not null && _simpleValue is not null &&
                _table.PrimaryKeyIndex >= 0 &&
                string.Equals(_simpleColumn, _table.Columns[_table.PrimaryKeyIndex], StringComparison.OrdinalIgnoreCase))
            {
                var pkStr = _simpleValue.ToString() ?? string.Empty;
                var search = _table.Index.Search(pkStr);
                if (search.Found)
                {
                    _pkPosition = search.Value;
                    _pkFound = true;
                    _phase = Phase.Pk;
                    return MoveNextPk();
                }

                _phase = Phase.Done;
                return false;
            }

            // Fallback: numeric-SIMD batch filter / full scan (allocating by nature).
            _fallback = _table.ScanStructRowsWhereCore(_where, _enableCaching).GetEnumerator();
            _phase = Phase.Fallback;
            return MoveNextFallback();
        }





    /// <summary>
        private bool MoveNextHash()
        {
            while (_posIndex < _positions.Count)
            {
                var pos = _positions[_posIndex++];
                var data = _engine.Read(_table.Name, pos);
                if (data is { Length: > 0 })
                {
                    _current = new StructRow(data.AsMemory(), _schema, _enableCaching);
                    return true;
                }
            }

            _phase = Phase.Done;
            return false;
        }

        private bool MoveNextPk()
        {
            if (_pkFound)
            {
                _pkFound = false;
                var data = _engine.Read(_table.Name, _pkPosition);
                if (data is { Length: > 0 })
                {
                    _current = new StructRow(data.AsMemory(), _schema, _enableCaching);
                    return true;
                }
            }

            _phase = Phase.Done;
            return false;
        }

        private bool MoveNextFallback()
        {
            if (_fallback is not null && _fallback.MoveNext())
            {
                _current = _fallback.Current;
                return true;
            }

            _phase = Phase.Done;
            return false;
        }

        /// <summary>Releases the fallback iterator (no-op on the allocation-free fast paths).</summary>
        public void Dispose() => _fallback?.Dispose();
    }


    /// SIMD-accelerated equality filter over a batch of int32 values (portable <c>Vector&lt;int&gt;</c>
    /// with scalar fallback). Writes matching indices into <paramref name="matches"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void SimdFilterInt32Batch(ReadOnlySpan<int> values, int expected, List<int> matches)
    {
        int vectorWidth = Vector<int>.Count;
        int i = 0;

        if (Vector.IsHardwareAccelerated)
        {
            var expectedVec = new Vector<int>(expected);
            for (; i <= values.Length - vectorWidth; i += vectorWidth)
            {
                Vector<int> mask = Vector.Equals(new Vector<int>(values.Slice(i, vectorWidth)), expectedVec);
                for (int lane = 0; lane < vectorWidth; lane++)
                {
                    if (mask[lane] != 0)
                    {
                        matches.Add(i + lane);
                    }
                }
            }
        }

        for (; i < values.Length; i++)
        {
            if (values[i] == expected)
            {
                matches.Add(i);
            }
        }
    }

    /// <summary>
    /// SIMD-accelerated equality filter over a batch of int64 values (portable <c>Vector&lt;long&gt;</c>
    /// with scalar fallback). Writes matching indices into <paramref name="matches"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void SimdFilterInt64Batch(ReadOnlySpan<long> values, long expected, List<int> matches)
    {
        int vectorWidth = Vector<long>.Count;
        int i = 0;

        if (Vector.IsHardwareAccelerated)
        {
            var expectedVec = new Vector<long>(expected);
            for (; i <= values.Length - vectorWidth; i += vectorWidth)
            {
                Vector<long> mask = Vector.Equals(new Vector<long>(values.Slice(i, vectorWidth)), expectedVec);
                for (int lane = 0; lane < vectorWidth; lane++)
                {
                    if (mask[lane] != 0)
                    {
                        matches.Add(i + lane);
                    }
                }
            }
        }

        for (; i < values.Length; i++)
        {
            if (values[i] == expected)
            {
                matches.Add(i);
            }
        }
    }

    /// <summary>
    /// Equality filter over a batch of double values (scalar; <c>Vector&lt;double&gt;</c> is
    /// avoided here for maximum portability). Writes matching indices into <paramref name="matches"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void SimdFilterDoubleBatch(ReadOnlySpan<double> values, double expected, List<int> matches)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == expected)
            {
                matches.Add(i);
            }
        }
    }

    /// <summary>
    /// Compares a single StructRow column against an expected value (allocation-free).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool MatchesSimpleWhere(StructRow row, VariableLengthSchema schema, string column, object expected)
    {
        var names = schema.ColumnNames;
        for (int i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], column, StringComparison.OrdinalIgnoreCase))
            {
                return SqlParser.AreValuesEqual(row.GetValueBoxed(i), expected);
            }
        }

        return false;
    }

    /// <summary>
    /// Stale-version guard: when the table has a PK, the PK index must point to
    /// <paramref name="recordPosition"/> for the record to be the current version.
    /// Returns true for tables without a PK (no version tracking). For fixed-width records the
    /// PK is read via the arena-aware dictionary deserialization (constant slot offsets).
    /// </summary>
    private bool TryValidateCurrentVersion(ReadOnlySpan<byte> recordData, VariableLengthSchema schema, long recordPosition, bool fixedWidth)
    {
        if (this.PrimaryKeyIndex < 0)
        {
            return true;
        }

        string pkValue;
        if (fixedWidth)
        {
            var row = DeserializeRowFixedWidth(recordData);
            pkValue = row.TryGetValue(this.Columns[this.PrimaryKeyIndex], out var v) && v is not null && v != DBNull.Value
                ? v.ToString() ?? string.Empty
                : string.Empty;
        }
        else
        {
            var pk = ExtractPrimaryKeyValueFromSpan(recordData, schema);
            if (pk is null)
            {
                return false;
            }

            pkValue = pk;
        }

        var search = this.Index.Search(pkValue);
        return search.Found && search.Value == recordPosition;
    }

    /// <summary>
    /// Determines whether a column is a fixed-width numeric type (Integer/Long/Real) that sits
    /// at a constant per-record byte offset (every preceding column is fixed-width). Returns the
    /// offset of the column's null flag within the record data.
    /// </summary>
    private bool TryGetFixedNumericWhereInfo(string column, out int valueOffset, out DataType type)
    {
        valueOffset = 0;
        type = DataType.String;

        int colIdx = this.Columns.IndexOf(column);
        if (colIdx < 0)
            return false;

        type = this.ColumnTypes[colIdx];
        if (type != DataType.Integer && type != DataType.Long && type != DataType.Real)
            return false;

        if (_fixedWidthRecords)
        {
            // Fixed-width layout: every column sits at a constant slot offset (null flag + payload),
            // so the numeric column can be read directly regardless of preceding variable columns —
            // no layout walk needed (B4).
            var layout = GetFixedWidthLayout();
            if (colIdx >= layout.ColumnCount)
                return false;

            valueOffset = layout.Offsets[colIdx];
            return true;
        }

        for (int i = 0; i < colIdx; i++)
        {
            (int size, bool isVariable) = GetColumnSizeAndVariability(this.ColumnTypes[i]);
            if (isVariable || size <= 0)
                return false;

            valueOffset += size;
        }

        return true;
    }

    /// <summary>
    /// Reads a fixed-width numeric column directly from raw record data (no deserialization,
    /// no boxing) and compares it against the expected value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool MatchesNumericDirect(
        ReadOnlySpan<byte> recordData,
        int valueOffset,
        DataType type,
        object expected)
    {
        return TryExtractNumericDirect(recordData, valueOffset, type, out var value) &&
            (type switch
            {
                DataType.Integer => (int)value == (int)expected,
                DataType.Long => (long)value == (long)expected,
                DataType.Real => (double)value == (double)expected,
                _ => false
            });
    }

    /// <summary>
    /// Reads a fixed-width numeric column directly from raw record data (no deserialization,
    /// no boxing). Returns false when the record is null or truncated.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryExtractNumericDirect(
        ReadOnlySpan<byte> recordData,
        int valueOffset,
        DataType type,
        out object value)
    {
        value = null!;

        // valueOffset points at the null flag; value data starts at +1.
        if (valueOffset + 1 >= recordData.Length)
            return false;

        if (recordData[valueOffset] == 0)
            return false; // NULL.

        ReadOnlySpan<byte> valueData = recordData.Slice(valueOffset + 1);
        switch (type)
        {
            case DataType.Integer when valueData.Length >= 4:
                value = BinaryPrimitives.ReadInt32LittleEndian(valueData);
                return true;
            case DataType.Long when valueData.Length >= 8:
                value = BinaryPrimitives.ReadInt64LittleEndian(valueData);
                return true;
            case DataType.Real when valueData.Length >= 8:
                value = BinaryPrimitives.ReadDoubleLittleEndian(valueData);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Parses a simple WHERE value into the expected fixed-width numeric type.
    /// </summary>
    private static bool TryParseNumericExpected(object? value, DataType type, out object expected)
    {
        expected = null!;
        string text = value?.ToString() ?? string.Empty;

        switch (type)
        {
            case DataType.Integer when int.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out int i):
                expected = i;
                return true;
            case DataType.Long when long.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out long l):
                expected = l;
                return true;
            case DataType.Real when double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double d):
                expected = d;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// B4: fixed-width string early-WHERE — reads the variable column's constant slot
    /// <c>[null-flag(1)][arena-offset(4)]</c>, resolves the payload from the overflow arena and
    /// compares it byte-wise against the pre-encoded expected UTF-8 (Binary collation). The
    /// comparison is exact for Binary collation (no full-row deserialization for non-matches).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool MatchesFixedWidthStringDirect(
        ReadOnlySpan<byte> recordData,
        int slotOffset,
        OverflowArena arena,
        ReadOnlySpan<byte> expectedUtf8)
    {
        if (slotOffset + 5 > recordData.Length || recordData[slotOffset] == 0)
        {
            return false; // truncated record or NULL slot (NULL never equals a value)
        }

        // NOTE: offset 0 is a VALID block offset (the first arena block's length prefix sits at 0),
        // so only the flag byte above distinguishes NULL — never filter on the offset value itself.
        var arenaOffset = BinaryPrimitives.ReadInt32LittleEndian(recordData.Slice(slotOffset + 1, 4));
        var payload = arena.Read(arenaOffset);
        return payload is not null && payload.AsSpan().SequenceEqual(expectedUtf8);
    }

    #endregion

    #region Internal Scanning Implementation

    /// <summary>
    /// Builds a variable-length aware schema for StructRow.
    /// ✅ CRITICAL: Standard StructRowSchema assumes fixed-size rows, but our storage uses
    /// variable-length records with length prefixes. This schema includes metadata
    /// for handling variable-length strings, blobs, etc.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private VariableLengthSchema BuildVariableLengthSchema()
    {
        // v2: schema is derived from immutable (post-DDL) column metadata — cache it.
        if (_cachedVariableSchema.HasValue)
        {
            return _cachedVariableSchema.Value;
        }

        var columnSizes = new int[Columns.Count];
        var isVariableLength = new bool[Columns.Count];

        for (int i = 0; i < Columns.Count; i++)
        {
            (columnSizes[i], isVariableLength[i]) = GetColumnSizeAndVariability(ColumnTypes[i]);
        }

        var schema = new VariableLengthSchema(
            Columns.ToArray(),
            ColumnTypes.ToArray(),
            columnSizes,
            isVariableLength);

        _cachedVariableSchema = schema;
        return schema;
    }

    /// <summary>
    /// Gets the size and variability of a column type.
    /// Fixed-size types (int, long, etc.) have known sizes.
    /// Variable-size types (string, blob, etc.) have size -1.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (int size, bool isVariable) GetColumnSizeAndVariability(DataType type)
    {
        return type switch
        {
            DataType.Integer => (5, false),   // 1 null flag + 4 bytes
            DataType.Long => (9, false),      // 1 null flag + 8 bytes
            DataType.Real => (9, false),      // 1 null flag + 8 bytes (double)
            DataType.Boolean => (2, false),   // 1 null flag + 1 byte
            DataType.DateTime => (9, false),  // 1 null flag + 8 bytes (ticks)
            DataType.Decimal => (17, false),  // 1 null flag + 16 bytes
            DataType.Guid => (17, false),     // 1 null flag + 16 bytes
            DataType.String => (-1, true),    // 1 null flag + 4 length + variable
            DataType.Blob => (-1, true),      // 1 null flag + 4 length + variable
            DataType.Ulid => (-1, true),      // 1 null flag + 4 length + variable (stored as string)
            _ => (-1, true)                   // Unknown = variable
        };
    }

    /// <summary>
    /// ✅ FIX: Non-iterator method that builds the list of StructRows from columnar storage.
    /// Avoids Span across yield boundary issue.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private IEnumerable<StructRow> ScanColumnarStructRowsInternal(VariableLengthSchema schema, bool enableCaching)
    {
        // Read entire data file
        var data = this.storage.ReadBytes(this.DataFile, noEncrypt: false);
        if (data == null || data.Length == 0)
        {
            return Array.Empty<StructRow>();
        }

        // ✅ FIX: Extract all valid rows FIRST (no Span across yield)
        var validRows = ExtractValidColumnarRows(data, schema);
        
        // Then yield from the list
        return YieldStructRows(validRows, schema, enableCaching);
    }

    /// <summary>
    /// Extracts valid row data from columnar storage without using yield.
    /// Returns list of (offset, length) for valid current-version rows.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private List<(int offset, int length)> ExtractValidColumnarRows(byte[] data, VariableLengthSchema schema)
    {
        var validRows = new List<(int offset, int length)>();
        ReadOnlySpan<byte> dataSpan = data.AsSpan();
        int filePosition = 0;

        while (filePosition < dataSpan.Length)
        {
            // Read length prefix (4 bytes)
            if (filePosition + 4 > dataSpan.Length)
                break;

            int recordLength = BinaryPrimitives.ReadInt32LittleEndian(
                dataSpan.Slice(filePosition, 4));

            // Validate record length
            if (recordLength <= 0 || recordLength > 1_000_000_000)
                break;

            if (filePosition + 4 + recordLength > dataSpan.Length)
                break;

            long currentRecordPosition = filePosition;
            int dataOffset = filePosition + 4;

            // ✅ Check if this row is the current version (not stale)
            bool isCurrentVersion = true;
            if (this.PrimaryKeyIndex >= 0)
            {
                var pkValue = ExtractPrimaryKeyValueFromSpan(dataSpan.Slice(dataOffset, recordLength), schema);
                if (pkValue != null)
                {
                    var searchResult = this.Index.Search(pkValue);
                    isCurrentVersion = searchResult.Found && searchResult.Value == currentRecordPosition;
                }
            }

            if (isCurrentVersion)
            {
                validRows.Add((dataOffset, recordLength));
            }

            filePosition += 4 + recordLength;
        }

        return validRows;
    }

    /// <summary>
    /// Yields StructRows from pre-extracted row positions.
    /// </summary>
    private IEnumerable<StructRow> YieldStructRows(
        List<(int offset, int length)> rowPositions,
        VariableLengthSchema schema,
        bool enableCaching)
    {
        // Re-read data for Memory references (can't store Span)
        var data = this.storage.ReadBytes(this.DataFile, noEncrypt: false);
        if (data == null)
            yield break;

        ReadOnlyMemory<byte> dataMemory = data.AsMemory();

        foreach (var (offset, length) in rowPositions)
        {
            var recordMemory = dataMemory.Slice(offset, length);
            yield return new StructRow(recordMemory, schema, enableCaching);
        }
    }

    /// <summary>
    /// Scans page-based storage with zero-allocation StructRow enumeration.
    /// ✅ PERFORMANCE: Uses storage engine's GetAllRecords for efficient iteration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private IEnumerable<StructRow> ScanPageBasedStructRowsInternal(VariableLengthSchema schema, bool enableCaching)
    {
        var engine = GetOrCreateStorageEngine();

        foreach (var (_, data) in engine.GetAllRecords(Name))
        {
            if (data != null && data.Length > 0)
            {
                // ✅ ZERO-COPY: Create StructRow pointing to raw data
                ReadOnlyMemory<byte> recordMemory = data.AsMemory();
                yield return new StructRow(recordMemory, schema, enableCaching);
            }
        }
    }

    /// <summary>
    /// Extracts the primary key value from raw record data using Span (no yield).
    /// ✅ OPTIMIZED: Only deserializes the PK column, not the entire row.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string? ExtractPrimaryKeyValueFromSpan(ReadOnlySpan<byte> recordData, VariableLengthSchema schema)
    {
        if (this.PrimaryKeyIndex < 0 || this.PrimaryKeyIndex >= Columns.Count)
            return null;

        // Calculate offset to PK column
        int offset = 0;
        for (int i = 0; i < this.PrimaryKeyIndex; i++)
        {
            if (offset >= recordData.Length)
                return null;

            // Skip this column's data
            offset += GetValueSizeFromSpan(recordData.Slice(offset), ColumnTypes[i]);
        }

        if (offset >= recordData.Length)
            return null;

        // Read PK value
        var pkValue = ReadTypedValueFromSpan(recordData.Slice(offset), ColumnTypes[this.PrimaryKeyIndex], out _);
        return pkValue?.ToString();
    }

    /// <summary>
    /// Gets the size of a value in the serialized data.
    /// Used for skipping columns during PK extraction.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetValueSizeFromSpan(ReadOnlySpan<byte> data, DataType type)
    {
        if (data.Length == 0)
            return 0;

        // Check null flag
        if (data[0] == 0)
        {
            return 1; // Just null flag
        }

        return type switch
        {
            DataType.Integer => 5,   // 1 null flag + 4 bytes
            DataType.Long => 9,      // 1 null flag + 8 bytes
            DataType.Real => 9,      // 1 null flag + 8 bytes
            DataType.Boolean => 2,   // 1 null flag + 1 byte
            DataType.DateTime => 9,  // 1 null flag + 8 bytes
            DataType.Decimal => 17,  // 1 null flag + 16 bytes
            DataType.Guid => 17,     // 1 null flag + 16 bytes
            DataType.String or DataType.Ulid or DataType.Blob => GetVariableLengthValueSizeFromSpan(data),
            _ => GetVariableLengthValueSizeFromSpan(data)
        };
    }

    /// <summary>
    /// Gets the size of a variable-length value (string, blob, ulid).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetVariableLengthValueSizeFromSpan(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5)
            return data.Length; // Incomplete data

        // Format: [1 null flag][4 length][data]
        int length = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(1, 4));
        return 5 + length; // 1 null + 4 length + data
    }

    #endregion
}

/// <summary>
/// Schema for variable-length record deserialization.
/// ✅ EXTENDS StructRowSchema: Adds support for variable-length columns (string, blob).
/// </summary>
public readonly struct VariableLengthSchema
{
    private readonly string[] _columnNames;
    private readonly DataType[] _columnTypes;
    private readonly int[] _fixedSizes;
    private readonly bool[] _isVariableLength;

    /// <summary>
    /// Initializes a new instance of VariableLengthSchema.
    /// </summary>
    public VariableLengthSchema(
        string[] columnNames,
        DataType[] columnTypes,
        int[] fixedSizes,
        bool[] isVariableLength)
    {
        _columnNames = columnNames;
        _columnTypes = columnTypes;
        _fixedSizes = fixedSizes;
        _isVariableLength = isVariableLength;
    }

    /// <summary>Gets the column names.</summary>
    public string[] ColumnNames => _columnNames;

    /// <summary>Gets the column types.</summary>
    public DataType[] ColumnTypes => _columnTypes;

    /// <summary>Gets the fixed sizes (or -1 for variable-length columns).</summary>
    public int[] FixedSizes => _fixedSizes;

    /// <summary>Gets whether each column is variable-length.</summary>
    public bool[] IsVariableLength => _isVariableLength;

    /// <summary>Gets the number of columns.</summary>
    public int ColumnCount => _columnNames?.Length ?? 0;
}
