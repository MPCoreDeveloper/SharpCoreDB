using System.Data;

namespace SharpCoreDB.Extensions;

/// <summary>
/// Provides type mapping between .NET types and database types for Dapper integration.
/// </summary>
public static class DapperTypeMapper
{
    private static readonly Dictionary<Type, DbType> _typeToDbType = new()
    {
        [typeof(byte)] = DbType.Byte,
        [typeof(sbyte)] = DbType.SByte,
        [typeof(short)] = DbType.Int16,
        [typeof(ushort)] = DbType.UInt16,
        [typeof(int)] = DbType.Int32,
        [typeof(uint)] = DbType.UInt32,
        [typeof(long)] = DbType.Int64,
        [typeof(ulong)] = DbType.UInt64,
        [typeof(float)] = DbType.Single,
        [typeof(double)] = DbType.Double,
        [typeof(decimal)] = DbType.Decimal,
        [typeof(bool)] = DbType.Boolean,
        [typeof(string)] = DbType.String,
        [typeof(char)] = DbType.StringFixedLength,
        [typeof(Guid)] = DbType.Guid,
        [typeof(DateTime)] = DbType.DateTime,
        [typeof(DateTimeOffset)] = DbType.DateTimeOffset,
        [typeof(TimeSpan)] = DbType.Time,
        [typeof(byte[])] = DbType.Binary,
    };

    private static readonly Dictionary<DbType, Type> _dbTypeToType = new()
    {
        [DbType.Byte] = typeof(byte),
        [DbType.SByte] = typeof(sbyte),
        [DbType.Int16] = typeof(short),
        [DbType.UInt16] = typeof(ushort),
        [DbType.Int32] = typeof(int),
        [DbType.UInt32] = typeof(uint),
        [DbType.Int64] = typeof(long),
        [DbType.UInt64] = typeof(ulong),
        [DbType.Single] = typeof(float),
        [DbType.Double] = typeof(double),
        [DbType.Decimal] = typeof(decimal),
        [DbType.Boolean] = typeof(bool),
        [DbType.String] = typeof(string),
        [DbType.StringFixedLength] = typeof(string),
        [DbType.Guid] = typeof(Guid),
        [DbType.DateTime] = typeof(DateTime),
        [DbType.DateTime2] = typeof(DateTime),
        [DbType.DateTimeOffset] = typeof(DateTimeOffset),
        [DbType.Time] = typeof(TimeSpan),
        [DbType.Binary] = typeof(byte[]),
    };

    /// <summary>
    /// Gets the DbType for a given .NET type.
    /// </summary>
    /// <param name="type">The .NET type.</param>
    /// <returns>The corresponding DbType.</returns>
    public static DbType GetDbType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (_typeToDbType.TryGetValue(underlyingType, out var dbType))
            return dbType;

        // Default to Object for unknown types
        return DbType.Object;
    }

    /// <summary>
    /// Gets the .NET type for a given DbType.
    /// </summary>
    /// <param name="dbType">The DbType.</param>
    /// <returns>The corresponding .NET type.</returns>
    public static Type GetClrType(DbType dbType)
    {
        if (_dbTypeToType.TryGetValue(dbType, out var type))
            return type;

        return typeof(object);
    }

    /// <summary>
    /// Converts a value to the target type.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="targetType">The target type.</param>
    /// <returns>The converted value.</returns>
    public static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null || value == DBNull.Value)
            return null;

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlyingType.IsAssignableFrom(value.GetType()))
            return value;

        try
        {
            // Handle Guid conversion
            if (underlyingType == typeof(Guid))
            {
                return value switch
                {
                    Guid g => g,
                    string s => Guid.Parse(s),
                    byte[] b when b.Length == 16 => new Guid(b),
                    _ => throw new InvalidCastException($"Cannot convert {value.GetType()} to Guid")
                };
            }

            // Handle byte array conversion
            if (underlyingType == typeof(byte[]))
            {
                return value switch
                {
                    byte[] b => b,
                    string s => Convert.FromBase64String(s),
                    _ => throw new InvalidCastException($"Cannot convert {value.GetType()} to byte[]")
                };
            }

            // Handle DateTime conversion
            if (underlyingType == typeof(DateTime))
            {
                return value switch
                {
                    DateTime dt => dt,
                    DateTimeOffset dto => dto.DateTime,
                    string s => DateTime.Parse(s),
                    long ticks => new DateTime(ticks),
                    _ => Convert.ToDateTime(value)
                };
            }

            // Handle TimeSpan conversion
            if (underlyingType == typeof(TimeSpan))
            {
                return value switch
                {
                    TimeSpan ts => ts,
                    string s => TimeSpan.Parse(s),
                    long ticks => new TimeSpan(ticks),
                    _ => throw new InvalidCastException($"Cannot convert {value.GetType()} to TimeSpan")
                };
            }

            // Use standard conversion
            return Convert.ChangeType(value, underlyingType);
        }
        catch (Exception ex)
        {
            throw new InvalidCastException(
                $"Failed to convert value of type {value.GetType()} to {targetType}", ex);
        }
    }

    /// <summary>
    /// Creates a typed parameter with automatic type inference.
    /// </summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The parameter value.</param>
    /// <returns>A DapperParameter with appropriate type.</returns>
    public static DapperParameter CreateParameter(string name, object? value)
    {
        var param = new DapperParameter
        {
            ParameterName = name,
            Value = value ?? DBNull.Value
        };

        if (value != null)
        {
            param.DbType = GetDbType(value.GetType());
            
            if (value is string str)
                param.Size = str.Length;
            else if (value is byte[] bytes)
                param.Size = bytes.Length;
        }

        return param;
    }

    /// <summary>
    /// Validates parameter compatibility with target type.
    /// </summary>
    /// <param name="value">The parameter value.</param>
    /// <param name="targetDbType">The target database type.</param>
    /// <returns>True if compatible, false otherwise.</returns>
    public static bool IsCompatible(object? value, DbType targetDbType)
    {
        if (value == null || value == DBNull.Value)
            return true;

        var valueDbType = GetDbType(value.GetType());
        
        // Same type is always compatible
        if (valueDbType == targetDbType)
            return true;

        // Check for numeric compatibility
        var numericTypes = new[] 
        { 
            DbType.Byte, DbType.SByte, DbType.Int16, DbType.UInt16, 
            DbType.Int32, DbType.UInt32, DbType.Int64, DbType.UInt64,
            DbType.Single, DbType.Double, DbType.Decimal 
        };

        if (numericTypes.Contains(valueDbType) && numericTypes.Contains(targetDbType))
            return true;

        // Check for string compatibility
        if (targetDbType is DbType.String or DbType.StringFixedLength or DbType.AnsiString or DbType.AnsiStringFixedLength)
            return true;

        return false;
    }
}

/// <summary>
/// Enhanced DapperParameter with additional metadata.
/// </summary>
public class EnhancedDapperParameter : DapperParameter
{
    /// <summary>
    /// Gets or sets the precision for decimal types.
    /// </summary>
    public byte Precision { get; set; }

    /// <summary>
    /// Gets or sets the scale for decimal types.
    /// </summary>
    public byte Scale { get; set; }

    /// <summary>
    /// Gets or sets whether the parameter is a Unicode string.
    /// </summary>
    public bool IsUnicode { get; set; } = true;

    /// <summary>
    /// Creates a parameter from a name-value pair with type inference.
    /// </summary>
    public static EnhancedDapperParameter FromValue(string name, object? value)
    {
        var baseParam = DapperTypeMapper.CreateParameter(name, value);
        
        return new EnhancedDapperParameter
        {
            ParameterName = baseParam.ParameterName,
            Value = baseParam.Value,
            DbType = baseParam.DbType,
            Size = baseParam.Size,
            Direction = ParameterDirection.Input,
            Precision = value is decimal ? (byte)28 : (byte)0,
            Scale = value is decimal ? (byte)8 : (byte)0
        };
    }
}
