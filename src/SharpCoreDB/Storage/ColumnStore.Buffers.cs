// <copyright file="ColumnStore.Buffers.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.ColumnStorage;

/// <summary>
/// Column buffer interface and concrete implementations for ColumnStore.
/// Provides type-specific storage for columnar data with optimizations per type.
/// </summary>
public sealed partial class ColumnStore<T>
{
    // Buffer interface and implementations defined in this file
}

/// <summary>
/// Base interface for column buffers.
/// </summary>
internal interface IColumnBuffer : IDisposable
{
    void SetValue(int index, object? value);
    int CountNonNull();
}

/// <summary>
/// Generic column buffer base class.
/// </summary>
public abstract class ColumnBuffer<TColumn> : IColumnBuffer where TColumn : struct
{
    /// <summary>
    /// The underlying data array.
    /// </summary>
    protected TColumn[] data;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ColumnBuffer{TColumn}"/> class.
    /// </summary>
    /// <param name="capacity">The buffer capacity.</param>
    protected ColumnBuffer(int capacity)
    {
        data = new TColumn[capacity];
    }

    /// <summary>
    /// Sets a value at the specified index.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <param name="value">The value to set.</param>
    public abstract void SetValue(int index, object? value);
    
    /// <summary>
    /// Gets the underlying data array.
    /// </summary>
    /// <returns>The data array.</returns>
    public TColumn[] GetData() => data;

    /// <summary>
    /// Counts non-null values in the buffer.
    /// </summary>
    /// <returns>The count of non-null values.</returns>
    public virtual int CountNonNull() => data.Length;

    /// <summary>
    /// Disposes the column buffer.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases resources used by the column buffer.
    /// </summary>
    /// <param name="disposing">True to release managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                data = Array.Empty<TColumn>();
            }
            _disposed = true;
        }
    }
}

/// <summary>
/// Int32 column buffer with SIMD-optimized operations.
/// </summary>
internal sealed class Int32ColumnBuffer : ColumnBuffer<int>
{
    public Int32ColumnBuffer(int capacity) : base(capacity) { }

    public override void SetValue(int index, object? value)
    {
        data[index] = value is int intVal ? intVal : Convert.ToInt32(value);
    }
}

/// <summary>
/// Int64 column buffer.
/// </summary>
internal sealed class Int64ColumnBuffer : ColumnBuffer<long>
{
    public Int64ColumnBuffer(int capacity) : base(capacity) { }

    public override void SetValue(int index, object? value)
    {
        data[index] = value is long longVal ? longVal : Convert.ToInt64(value);
    }
}

/// <summary>
/// Double column buffer with SIMD support.
/// </summary>
internal sealed class DoubleColumnBuffer : ColumnBuffer<double>
{
    public DoubleColumnBuffer(int capacity) : base(capacity) { }

    public override void SetValue(int index, object? value)
    {
        data[index] = value is double doubleVal ? doubleVal : Convert.ToDouble(value);
    }
}

/// <summary>
/// Decimal column buffer (no SIMD, but high precision).
/// </summary>
internal sealed class DecimalColumnBuffer : ColumnBuffer<decimal>
{
    public DecimalColumnBuffer(int capacity) : base(capacity) { }

    public override void SetValue(int index, object? value)
    {
        data[index] = value is decimal decVal ? decVal : Convert.ToDecimal(value);
    }
}

/// <summary>
/// DateTime column buffer (stored as ticks for fast comparison).  // Note: Actually stores ToBinary() for compatibility
/// </summary>
internal sealed class DateTimeColumnBuffer : ColumnBuffer<long>
{
    public DateTimeColumnBuffer(int capacity) : base(capacity) { }

    public override void SetValue(int index, object? value)
    {
        // ✅ FIX: Use ToBinary() format for consistency with other serialization paths
        // Previously used Ticks which caused format mismatch with ReadTypedValueFromSpan
        data[index] = value is DateTime dt ? dt.ToBinary() : 0;  // ✅ ToBinary() not Ticks
    }
}

/// <summary>
/// Bool column buffer (bit-packed for efficiency).
/// </summary>
internal sealed class BoolColumnBuffer : IColumnBuffer
{
    private readonly byte[] data;
    private readonly int capacity;

    public BoolColumnBuffer(int capacity)
    {
        this.capacity = capacity;
        data = new byte[(capacity + 7) / 8]; // Bit-pack: 8 bools per byte
    }

    public void SetValue(int index, object? value)
    {
        if (value is bool boolVal && boolVal)
        {
            int byteIndex = index / 8;
            int bitIndex = index % 8;
            data[byteIndex] |= (byte)(1 << bitIndex);
        }
    }

    public int CountNonNull() => capacity;

    public void Dispose() { }
}

/// <summary>
/// String column buffer with dictionary encoding potential.
/// </summary>
internal sealed class StringColumnBuffer : IColumnBuffer
{
    private readonly string?[] data;

    public StringColumnBuffer(int capacity)
    {
        data = new string?[capacity];
    }

    public void SetValue(int index, object? value)
    {
        data[index] = value?.ToString();
    }

    public int CountNonNull() => data.Count(s => s != null);

    public void Dispose() { }
}

/// <summary>
/// Generic object column buffer (fallback for unsupported types).
/// </summary>
internal sealed class ObjectColumnBuffer : IColumnBuffer
{
    private readonly object?[] data;

    public ObjectColumnBuffer(int capacity)
    {
        data = new object?[capacity];
    }

    public void SetValue(int index, object? value)
    {
        data[index] = value;
    }

    public int CountNonNull() => data.Count(o => o != null);

    public void Dispose() { }
}
