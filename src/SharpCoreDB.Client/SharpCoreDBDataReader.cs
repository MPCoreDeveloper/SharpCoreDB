// <copyright file="SharpCoreDBDataReader.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using System.Collections;
using System.Data;
using System.Data.Common;
using Grpc.Core;
using SharpCoreDB.Server.Protocol;

namespace SharpCoreDB.Client;

/// <summary>
/// ADO.NET DataReader for SharpCoreDB network server query results.
/// Streams results from gRPC server streaming call.
/// C# 14: Uses async streaming and pattern matching.
/// </summary>
public sealed class SharpCoreDBDataReader : DbDataReader
{
    private readonly IAsyncStreamReader<QueryResponse> _responseStream;
    private readonly CancellationToken _cancellationToken;
    private QueryResponse? _currentResponse;
    private int _currentRowIndex = -1;
    private bool _isClosed;
    private bool _hasReadFirstResponse;

    /// <summary>
    /// Gets the column metadata.
    /// </summary>
    public IReadOnlyList<ColumnMetadata> Columns { get; private set; } = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBDataReader"/> class.
    /// </summary>
    public SharpCoreDBDataReader(IAsyncStreamReader<QueryResponse> responseStream, CancellationToken cancellationToken)
    {
        _responseStream = responseStream ?? throw new ArgumentNullException(nameof(responseStream));
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Gets the value at the specified ordinal.
    /// </summary>
    public override object this[int ordinal] => GetValue(ordinal);

    /// <summary>
    /// Gets the value with the specified name.
    /// </summary>
    public override object this[string name] => GetValue(GetOrdinal(name));

    /// <summary>
    /// Advances to the next row in the result set.
    /// </summary>
    public override async Task<bool> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (_isClosed)
        {
            return false;
        }

        // Read first response if not done yet
        if (!_hasReadFirstResponse)
        {
            if (!await _responseStream.MoveNext(cancellationToken))
            {
                return false;
            }

            _currentResponse = _responseStream.Current;
            Columns = _currentResponse.Columns;
            _hasReadFirstResponse = true;
            _currentRowIndex = -1;
        }

        var currentResponse = _currentResponse ?? throw new InvalidOperationException("No current response");

        // Move to next row in current response
        _currentRowIndex++;

        if (_currentRowIndex < currentResponse.Rows.Count)
        {
            return true;
        }

        // Try to read next response
        if (await _responseStream.MoveNext(cancellationToken))
        {
            _currentResponse = _responseStream.Current;
            _currentRowIndex = 0;
            return _currentResponse.Rows.Count > 0;
        }

        return false;
    }

    /// <summary>
    /// Gets the number of columns in the current row.
    /// </summary>
    public override int FieldCount => Columns.Count;

    /// <summary>
    /// Gets the name of the column at the specified ordinal.
    /// </summary>
    public override string GetName(int ordinal)
    {
        ValidateOrdinal(ordinal);
        return Columns[ordinal].Name;
    }

    /// <summary>
    /// Gets the data type of the column at the specified ordinal.
    /// </summary>
    public override Type GetFieldType(int ordinal)
    {
        ValidateOrdinal(ordinal);
        return Columns[ordinal].Type switch
        {
            DataType.Integer => typeof(int),
            DataType.Long => typeof(long),
            DataType.Real => typeof(double),
            DataType.String => typeof(string),
            DataType.Blob => typeof(byte[]),
            DataType.Boolean => typeof(bool),
            DataType.Datetime => typeof(DateTime),
            DataType.Guid => typeof(string),
            DataType.Ulid => typeof(string),
            DataType.Rowref => typeof(string),
            DataType.Vector => typeof(double[]),
            DataType.Decimal => typeof(decimal),
            _ => typeof(object)
        };
    }

    /// <summary>
    /// Gets the value of the column at the specified ordinal.
    /// </summary>
    public override object GetValue(int ordinal)
    {
        ValidateOrdinal(ordinal);
        if (_currentResponse == null || _currentRowIndex < 0 || _currentRowIndex >= _currentResponse.Rows.Count)
        {
            throw new InvalidOperationException("No current row");
        }

        var row = _currentResponse.Rows[_currentRowIndex];
        return ConvertParameterValue(row.Values[ordinal]);
    }

    /// <summary>
    /// Gets whether the column at the specified ordinal is DBNull.
    /// </summary>
    public override bool IsDBNull(int ordinal)
    {
        ValidateOrdinal(ordinal);
        if (_currentResponse == null || _currentRowIndex < 0 || _currentRowIndex >= _currentResponse.Rows.Count)
        {
            return true;
        }

        var row = _currentResponse.Rows[_currentRowIndex];
        var value = row.Values[ordinal];

        // Check if the oneof is not set (represents null)
        return value.ValueCase == ParameterValue.ValueOneofCase.None;
    }

    /// <summary>
    /// Gets the ordinal of the column with the specified name.
    /// </summary>
    public override int GetOrdinal(string name)
    {
        for (int i = 0; i < Columns.Count; i++)
        {
            if (string.Equals(Columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        throw new IndexOutOfRangeException($"Column '{name}' not found");
    }

    /// <summary>
    /// Closes the reader.
    /// </summary>
    public override void Close()
    {
        _isClosed = true;
    }

    /// <summary>
    /// Gets whether the reader is closed.
    /// </summary>
    public override bool IsClosed => _isClosed;

    /// <summary>
    /// Gets the depth of nesting for the current row.
    /// </summary>
    public override int Depth => 0;

    /// <summary>
    /// Gets whether the reader has rows.
    /// </summary>
    public override bool HasRows => !_isClosed && _hasReadFirstResponse;

    /// <summary>
    /// Gets the number of rows changed, inserted, or deleted by execution of the SQL statement.
    /// </summary>
    public override int RecordsAffected => _currentResponse?.RowsAffected ?? 0;

    // Required abstract method implementations
    public override bool Read() => throw new NotSupportedException("Use ReadAsync");
    public override DataTable GetSchemaTable() => throw new NotSupportedException();
    public override bool NextResult() => throw new NotSupportedException();
    public override int GetValues(object[] values) => throw new NotSupportedException();
    public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;
    public override IEnumerator GetEnumerator()
    {
        // We can't enumerate the result set directly, as it is async and paged.
        // Instead, we throw an exception to signal the limitation.
        throw new NotSupportedException("Direct enumeration is not supported. Use ReadAsync instead.");
    }

    // Typed getters
    public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
    public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
    public override char GetChar(int ordinal) => (char)GetValue(ordinal);
    public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
    public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
    public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
    public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
    public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
    public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
    public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
    public override string GetString(int ordinal) => (string)GetValue(ordinal);
    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);

    // Stream getters (not supported for network protocol)
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        Close();
        await ValueTask.CompletedTask;
    }

    private void ValidateOrdinal(int ordinal)
    {
        if (ordinal < 0 || ordinal >= Columns.Count)
        {
            throw new IndexOutOfRangeException($"Ordinal {ordinal} is out of range");
        }
    }

    private static object ConvertParameterValue(ParameterValue value)
    {
        return value.ValueCase switch
        {
            ParameterValue.ValueOneofCase.IntValue => value.IntValue,
            ParameterValue.ValueOneofCase.LongValue => value.LongValue,
            ParameterValue.ValueOneofCase.DoubleValue => value.DoubleValue,
            ParameterValue.ValueOneofCase.StringValue => value.StringValue,
            ParameterValue.ValueOneofCase.BytesValue => value.BytesValue.ToByteArray(),
            ParameterValue.ValueOneofCase.BoolValue => value.BoolValue,
            ParameterValue.ValueOneofCase.TimestampValue => value.TimestampValue.ToDateTime(),
            ParameterValue.ValueOneofCase.GuidValue => Guid.Parse(value.GuidValue),
            ParameterValue.ValueOneofCase.UlidValue => value.UlidValue,
            ParameterValue.ValueOneofCase.VectorValue => value.VectorValue.Values.Select(v => (float)v).ToArray(),
            ParameterValue.ValueOneofCase.RowrefValue => $"{value.RowrefValue.TableName}:{value.RowrefValue.RowId}",
            _ => DBNull.Value
        };
    }
}
