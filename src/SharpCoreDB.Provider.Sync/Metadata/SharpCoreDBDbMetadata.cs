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
    public static DbType MapDataType(int dataType) // Would be DataType enum in actual use
    {
        // TODO: Implement in Phase 1.3
        // Map: DataType.INTEGER -> DbType.Int64
        //      DataType.TEXT -> DbType.String
        //      DataType.REAL -> DbType.Double
        //      DataType.BLOB -> DbType.Binary
        //      etc.
        throw new NotImplementedException("Phase 1.3: Type mapping implementation pending");
    }

    /// <summary>
    /// Gets the CLR type for a given DbType.
    /// </summary>
    /// <param name="dbType">DbType to map</param>
    /// <returns>Corresponding CLR Type</returns>
    public static Type GetClrType(DbType dbType)
    {
        // TODO: Implement in Phase 1.3
        return dbType switch
        {
            DbType.Int64 => typeof(long),
            DbType.String => typeof(string),
            DbType.Double => typeof(double),
            DbType.Binary => typeof(byte[]),
            DbType.Boolean => typeof(bool),
            DbType.DateTime => typeof(DateTime),
            DbType.Decimal => typeof(decimal),
            _ => typeof(object)
        };
    }
}
