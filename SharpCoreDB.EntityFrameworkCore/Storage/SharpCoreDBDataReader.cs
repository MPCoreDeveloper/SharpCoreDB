using System.Data;
using System.Data.Common;

namespace SharpCoreDB.EntityFrameworkCore.Storage;

/// <summary>
/// Simplified data reader for SharpCoreDB.
/// NOTE: This is a minimal implementation. Full implementation requires query result parsing.
/// </summary>
public class SharpCoreDBDataReader : DbDataReader
{
    private bool _closed;

    /// <inheritdoc />
    public override object this[int ordinal] => throw new NotImplementedException();

    /// <inheritdoc />
    public override object this[string name] => throw new NotImplementedException();

    /// <inheritdoc />
    public override int Depth => 0;

    /// <inheritdoc />
    public override int FieldCount => 0;

    /// <inheritdoc />
    public override bool HasRows => false;

    /// <inheritdoc />
    public override bool IsClosed => _closed;

    /// <inheritdoc />
    public override int RecordsAffected => 0;

    /// <inheritdoc />
    public override bool GetBoolean(int ordinal) => throw new NotImplementedException();

    /// <inheritdoc />
    public override byte GetByte(int ordinal) => throw new NotImplementedException();

    /// <inheritdoc />
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
        => throw new NotImplementedException();

    /// <inheritdoc />
    public override char GetChar(int ordinal) => throw new NotImplementedException();

    /// <inheritdoc />
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
        => throw new NotImplementedException();

    /// <inheritdoc />
    public override string GetDataTypeName(int ordinal) => throw new NotImplementedException();

    /// <inheritdoc />
    public override DateTime GetDateTime(int ordinal) => throw new NotImplementedException();

    /// <inheritdoc />
    public override decimal GetDecimal(int ordinal) => throw new NotImplementedException();

    /// <inheritdoc />
    public override double GetDouble(int ordinal) => throw new NotImplementedException();

    /// <inheritdoc />
    public override Type GetFieldType(int ordinal) => throw new NotImplementedException();

    /// <inheritdoc />
    public override float GetFloat(int ordinal) => throw new NotImplementedException();

    /// <inheritdoc />
    public override Guid GetGuid(int ordinal) => throw new NotImplementedException();

    /// <inheritdoc />
    public override short GetInt16(int ordinal) => throw new NotImplementedException();

    /// <inheritdoc />
    public override int GetInt32(int ordinal) => throw new NotImplementedException();

    /// <inheritdoc />
    public override long GetInt64(int ordinal) => throw new NotImplementedException();

    /// <inheritdoc />
    public override string GetName(int ordinal) => throw new NotImplementedException();

    /// <inheritdoc />
    public override int GetOrdinal(string name) => throw new NotImplementedException();

    /// <inheritdoc />
    public override string GetString(int ordinal) => throw new NotImplementedException();

    /// <inheritdoc />
    public override object GetValue(int ordinal) => throw new NotImplementedException();

    /// <inheritdoc />
    public override int GetValues(object[] values) => throw new NotImplementedException();

    /// <inheritdoc />
    public override bool IsDBNull(int ordinal) => throw new NotImplementedException();

    /// <inheritdoc />
    public override bool NextResult() => false;

    /// <inheritdoc />
    public override bool Read() => false;

    /// <inheritdoc />
    public override System.Collections.IEnumerator GetEnumerator()
        => throw new NotImplementedException();

    /// <inheritdoc />
    public override void Close()
    {
        _closed = true;
    }
}
