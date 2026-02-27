#nullable enable

using System.Data;

namespace SharpCoreDB.Provider.Sync.Metadata;

/// <summary>
/// Provides type mapping between SharpCoreDB DataTypes, System.Data.DbType, and .NET types.
/// Validates data types for sync compatibility and handles nullable references.
/// </summary>
public sealed class SharpCoreDBDbMetadata
{
    /// <summary>
    /// Maps a SharpCoreDB DataType to the corresponding .NET DbType.
    /// </summary>
    /// <param name="dataType">SharpCoreDB DataType enum value</param>
    /// <returns>Corresponding DbType</returns>
    /// <exception cref="NotSupportedException">If the DataType is not supported for sync</exception>
    public static DbType MapDataType(DataType dataType) => dataType switch
    {
        DataType.Integer => DbType.Int32,
        DataType.Long => DbType.Int64,
        DataType.String => DbType.String,
        DataType.Real => DbType.Double,
        DataType.Blob => DbType.Binary,
        DataType.Boolean => DbType.Boolean,
        DataType.DateTime => DbType.DateTime,
        DataType.Decimal => DbType.Decimal,
        DataType.Ulid => DbType.String,     // Store as TEXT
        DataType.Guid => DbType.Guid,
        DataType.RowRef => DbType.Int64,    // Store as 64-bit reference
        DataType.Vector => throw new NotSupportedException("Vector type is not supported for sync. Use BLOB serialization."),
        _ => throw new NotSupportedException($"DataType '{dataType}' is not supported for sync.")
    };

    /// <summary>
    /// Maps a DbType to SharpCoreDB DataType.
    /// </summary>
    /// <param name="dbType">System.Data.DbType</param>
    /// <returns>Corresponding SharpCoreDB DataType</returns>
    public static DataType MapDbType(DbType dbType) => dbType switch
    {
        DbType.Int32 => DataType.Integer,
        DbType.Int64 => DataType.Long,
        DbType.String => DataType.String,
        DbType.AnsiString => DataType.String,
        DbType.StringFixedLength => DataType.String,
        DbType.AnsiStringFixedLength => DataType.String,
        DbType.Double => DataType.Real,
        DbType.Binary => DataType.Blob,
        DbType.Boolean => DataType.Boolean,
        DbType.DateTime => DataType.DateTime,
        DbType.DateTime2 => DataType.DateTime,
        DbType.DateTimeOffset => DataType.DateTime,
        DbType.Decimal => DataType.Decimal,
        DbType.Guid => DataType.Guid,
        _ => throw new NotSupportedException($"DbType '{dbType}' has no direct SharpCoreDB mapping.")
    };

    /// <summary>
    /// Gets the CLR type for a given DbType.
    /// </summary>
    /// <param name="dbType">DbType to map</param>
    /// <returns>Corresponding CLR Type</returns>
    public static Type GetClrType(DbType dbType) => dbType switch
    {
        DbType.Int32 => typeof(int),
        DbType.Int64 => typeof(long),
        DbType.String => typeof(string),
        DbType.AnsiString => typeof(string),
        DbType.StringFixedLength => typeof(string),
        DbType.AnsiStringFixedLength => typeof(string),
        DbType.Double => typeof(double),
        DbType.Binary => typeof(byte[]),
        DbType.Boolean => typeof(bool),
        DbType.DateTime => typeof(DateTime),
        DbType.DateTime2 => typeof(DateTime),
        DbType.DateTimeOffset => typeof(DateTimeOffset),
        DbType.Decimal => typeof(decimal),
        DbType.Guid => typeof(Guid),
        DbType.Single => typeof(float),
        DbType.Int16 => typeof(short),
        DbType.Byte => typeof(byte),
        _ => typeof(object)
    };

    /// <summary>
    /// Gets the CLR type for a SharpCoreDB DataType.
    /// </summary>
    /// <param name="dataType">SharpCoreDB DataType</param>
    /// <returns>Corresponding CLR Type</returns>
    public static Type GetClrType(DataType dataType) => dataType switch
    {
        DataType.Integer => typeof(int),
        DataType.Long => typeof(long),
        DataType.String => typeof(string),
        DataType.Real => typeof(double),
        DataType.Blob => typeof(byte[]),
        DataType.Boolean => typeof(bool),
        DataType.DateTime => typeof(DateTime),
        DataType.Decimal => typeof(decimal),
        DataType.Ulid => typeof(string),
        DataType.Guid => typeof(Guid),
        DataType.RowRef => typeof(long),
        DataType.Vector => throw new NotSupportedException("Vector type has no single CLR type (use float[])"),
        _ => typeof(object)
    };

    /// <summary>
    /// Converts a SharpCoreDB DataType to SQL type string for DDL generation.
    /// </summary>
    /// <param name="dataType">SharpCoreDB DataType</param>
    /// <returns>SQL type string (e.g., "INTEGER", "TEXT", "REAL")</returns>
    public static string ToSqlTypeString(DataType dataType) => dataType switch
    {
        DataType.Integer => "INTEGER",
        DataType.Long => "BIGINT",
        DataType.String => "TEXT",
        DataType.Real => "REAL",
        DataType.Blob => "BLOB",
        DataType.Boolean => "INTEGER",  // SQLite convention: 0/1
        DataType.DateTime => "TEXT",    // ISO8601 string
        DataType.Decimal => "TEXT",     // Store as string to preserve precision
        DataType.Ulid => "TEXT",
        DataType.Guid => "TEXT",
        DataType.RowRef => "BIGINT",
        DataType.Vector => throw new NotSupportedException("Vector type should be serialized as BLOB"),
        _ => "TEXT"
    };

    /// <summary>
    /// Validates if a DataType is supported for synchronization.
    /// </summary>
    /// <param name="dataType">DataType to validate</param>
    /// <returns>True if supported, false otherwise</returns>
    public static bool IsSyncSupported(DataType dataType) =>
        dataType != DataType.Vector; // Vector requires custom serialization
}
