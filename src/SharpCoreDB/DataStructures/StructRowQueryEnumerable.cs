#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;

namespace SharpCoreDB.DataStructures;

/// <summary>
/// Zero-allocation enumerable returned by <c>ExecuteQueryStruct</c>. Foreach on this concrete
/// type uses <see cref="StructRowQueryEnumerator"/> (no heap allocation); treating it as
/// <c>IEnumerable&lt;StructRow&gt;</c> (LINQ, boxing) uses a small class-based enumerator.
/// </summary>
public readonly struct StructRowQueryEnumerable : IEnumerable<StructRow>
{
    private readonly Table? _table;
    private readonly string? _where;
    private readonly bool _hasRows;
    private readonly int _skipped;
    private readonly int? _limit;

    internal StructRowQueryEnumerable(Table? table, string? where, bool hasRows, int skipped, int? limit)
    {
        _table = table;
        _where = where;
        _hasRows = hasRows;
        _skipped = skipped;
        _limit = limit;
    }

    /// <summary>Gets the allocation-free enumerator.</summary>
    public StructRowQueryEnumerator GetEnumerator() => new(_table, _where, _hasRows, _skipped, _limit);

    IEnumerator<StructRow> IEnumerable<StructRow>.GetEnumerator()
        => new BoxedEnumerator(_table, _where, _hasRows, _skipped, _limit);

    IEnumerator IEnumerable.GetEnumerator()
        => ((IEnumerable<StructRow>)this).GetEnumerator();

    private sealed class BoxedEnumerator : IEnumerator<StructRow>
    {
        // NOT readonly: MoveNext mutates the struct enumerator's state.
        private StructRowQueryEnumerator _inner;

        internal BoxedEnumerator(Table? table, string? where, bool hasRows, int skipped, int? limit)
        {
            _inner = new StructRowQueryEnumerator(table, where, hasRows, skipped, limit);
        }

        public StructRow Current => _inner.Current;
        object IEnumerator.Current => _inner.Current;

        public bool MoveNext() => _inner.MoveNext();
        public void Reset() => throw new NotSupportedException();
        public void Dispose() => _inner.Dispose();
    }
}

/// <summary>
/// Allocation-free enumerator for <see cref="StructRowQueryEnumerable"/>. Drives the table-level
/// <see cref="Table.StructRowWhereEnumerator"/> and applies LIMIT/OFFSET.
/// </summary>
public struct StructRowQueryEnumerator : IDisposable
{
    private readonly Table? _table;
    private readonly string? _where;
    private readonly bool _hasRows;
    private readonly int _skipped;
    private readonly int? _limit;
    private Table.StructRowWhereEnumerator _rows;
    private int _index;
    private bool _initialized;
    private StructRow _current;

    internal StructRowQueryEnumerator(Table? table, string? where, bool hasRows, int skipped, int? limit)
    {
        _table = table;
        _where = where;
        _hasRows = hasRows;
        _skipped = skipped;
        _limit = limit;
        _rows = default;
        _index = 0;
        _initialized = false;
        _current = default;
    }

    /// <summary>Gets the current row.</summary>
    public StructRow Current => _current;

    /// <summary>Advances to the next row (applies OFFSET then LIMIT).</summary>
    public bool MoveNext()
    {
        if (!_initialized)
        {
            _initialized = true;
            if (!_hasRows || _table is null)
            {
                return false;
            }

            _rows = _table.ScanStructRowsWhere(_where).GetEnumerator();
            _index = 0;
        }

        while (_rows.MoveNext())
        {
            if (_index < _skipped)
            {
                _index++;
                continue;
            }

            if (_limit.HasValue && _index - _skipped >= _limit.Value)
            {
                return false;
            }

            _index++;
            _current = _rows.Current;
            return true;
        }

        return false;
    }

    /// <summary>Releases the table-level enumerator (no-op on the allocation-free fast paths).</summary>
    public void Dispose() => _rows.Dispose();
}
