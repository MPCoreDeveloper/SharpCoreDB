using System.Data;
using System.Data.Common;
using SharpCoreDB; // for Ulid

namespace SharpCoreDB.Data.Provider;

/// <summary>
/// Represents a parameter to a <see cref="SharpCoreDBCommand"/>.
/// </summary>
public sealed class SharpCoreDBParameter : DbParameter
{
    private object? _value;
    private bool _isNullable;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBParameter"/> class.
    /// </summary>
    public SharpCoreDBParameter()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBParameter"/> class with a parameter name and value.
    /// </summary>
    public SharpCoreDBParameter(string parameterName, object? value)
    {
        ParameterName = parameterName;
        Value = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBParameter"/> class with a parameter name and type.
    /// </summary>
    public SharpCoreDBParameter(string parameterName, DbType type)
    {
        ParameterName = parameterName;
        DbType = type;
    }

    /// <summary>
    /// Gets or sets the <see cref="DbType"/> of the parameter.
    /// </summary>
    public override DbType DbType { get; set; } = DbType.String;

    /// <summary>
    /// Gets or sets the direction of the parameter.
    /// </summary>
    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

    /// <summary>
    /// Gets or sets a value indicating whether the parameter is nullable.
    /// </summary>
    public override bool IsNullable
    {
        get => _isNullable;
        set => _isNullable = value;
    }

    /// <summary>
    /// Gets or sets the name of the parameter.
    /// </summary>
    public override string ParameterName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the size of the parameter.
    /// </summary>
    public override int Size { get; set; }

    /// <summary>
    /// Gets or sets the source column name.
    /// </summary>
    public override string SourceColumn { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the source column is nullable.
    /// </summary>
    public override bool SourceColumnNullMapping { get; set; }

    /// <summary>
    /// Gets or sets the <see cref="DataRowVersion"/> to use.
    /// </summary>
    public override DataRowVersion SourceVersion { get; set; } = DataRowVersion.Current;

    /// <summary>
    /// Gets or sets the value of the parameter.
    /// </summary>
    public override object? Value
    {
        get => _value;
        set
        {
            _value = value;
            
            // Infer DbType from value if not explicitly set
            if (value != null && DbType == DbType.String)
            {
                DbType = InferDbType(value);
            }
        }
    }

    /// <summary>
    /// Resets the DbType property to its original value.
    /// </summary>
    public override void ResetDbType()
    {
        DbType = DbType.String;
    }

    private static DbType InferDbType(object value)
    {
        return value switch
        {
            int => DbType.Int32,
            long => DbType.Int64,
            string => DbType.String,
            bool => DbType.Boolean,
            DateTime => DbType.DateTime,
            decimal => DbType.Decimal,
            double => DbType.Double,
            float => DbType.Single,
            Guid => DbType.Guid,
            Ulid => DbType.String, // ULID stored as 26-char string
            byte[] => DbType.Binary,
            _ => DbType.Object
        };
    }
}
