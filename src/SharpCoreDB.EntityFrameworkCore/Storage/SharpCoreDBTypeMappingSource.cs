using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using System.Reflection;

namespace SharpCoreDB.EntityFrameworkCore.Storage;

/// <summary>
/// Type mapping source for SharpCoreDB.
/// Maps .NET types to SharpCoreDB SQL types (INTEGER, TEXT, REAL, DATETIME, etc.).
/// </summary>
public class SharpCoreDBTypeMappingSource : RelationalTypeMappingSource
{
    private static readonly Dictionary<Type, RelationalTypeMapping> _typeMappings = new()
    {
        [typeof(int)] = new IntTypeMapping("INTEGER"),
        [typeof(long)] = new LongTypeMapping("LONG"),
        [typeof(string)] = new StringTypeMapping("TEXT", System.Data.DbType.String),
        [typeof(bool)] = new BoolTypeMapping("BOOLEAN"),
        [typeof(double)] = new DoubleTypeMapping("REAL"),
        [typeof(float)] = new FloatTypeMapping("REAL"),
        [typeof(decimal)] = new DecimalTypeMapping("DECIMAL"),
        [typeof(DateTime)] = new DateTimeTypeMapping("DATETIME"),
        [typeof(DateTimeOffset)] = new DateTimeOffsetTypeMapping("DATETIME"),
        [typeof(TimeSpan)] = new TimeSpanTypeMapping("TEXT"),
        [typeof(DateOnly)] = new DateOnlyTypeMapping("TEXT"),
        [typeof(TimeOnly)] = new TimeOnlyTypeMapping("TEXT"),
        [typeof(Guid)] = new GuidTypeMapping("GUID"),
        [typeof(byte[])] = new ByteArrayTypeMapping("BLOB"),
        [typeof(byte)] = new ByteTypeMapping("INTEGER"),
        [typeof(short)] = new ShortTypeMapping("INTEGER"),
        [typeof(sbyte)] = new SByteTypeMapping("INTEGER"),
        [typeof(ushort)] = new UShortTypeMapping("INTEGER"),
        [typeof(uint)] = new UIntTypeMapping("INTEGER"),
        [typeof(ulong)] = new ULongTypeMapping("LONG"),
        [typeof(char)] = new CharTypeMapping("TEXT")
    };

    /// <summary>
    /// Initializes a new instance of the SharpCoreDBTypeMappingSource class.
    /// </summary>
    public SharpCoreDBTypeMappingSource(TypeMappingSourceDependencies dependencies, RelationalTypeMappingSourceDependencies relationalDependencies)
        : base(dependencies, relationalDependencies)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        var clrType = mappingInfo.ClrType;
        if (clrType != null && _typeMappings.TryGetValue(clrType, out var mapping))
        {
            return mapping;
        }

        if (mappingInfo.StoreTypeName != null)
        {
            return mappingInfo.StoreTypeName.ToUpperInvariant() switch
            {
                "INTEGER" => new IntTypeMapping("INTEGER"),
                "LONG" => new LongTypeMapping("LONG"),
                "TEXT" => new StringTypeMapping("TEXT", System.Data.DbType.String),
                "BOOLEAN" => new BoolTypeMapping("BOOLEAN"),
                "REAL" => new DoubleTypeMapping("REAL"),
                "DECIMAL" => new DecimalTypeMapping("DECIMAL"),
                "DATETIME" => new DateTimeTypeMapping("DATETIME"),
                "GUID" => new GuidTypeMapping("GUID"),
                "ULID" => new StringTypeMapping("ULID", System.Data.DbType.String),
                "BLOB" => new ByteArrayTypeMapping("BLOB"),
                "FLOAT" => new FloatTypeMapping("REAL"),
                _ => null
            };
        }

        return base.FindMapping(mappingInfo);
    }

    /// <inheritdoc />
    /// <summary>
    /// Finds type mapping for a property.
    /// ✅ EF Core COLLATE: Collation is handled automatically by EF Core via UseCollation() - no custom mapping needed.
    /// </summary>
    public override CoreTypeMapping? FindMapping(IProperty property)
    {
        // EF Core automatically applies collation from property metadata
        // No need to manually clone the mapping here
        return FindMapping(property.ClrType);
    }

    /// <inheritdoc />
    public override CoreTypeMapping? FindMapping(IElementType elementType)
    {
        return FindMapping(elementType.ClrType);
    }

    /// <inheritdoc />
    public override RelationalTypeMapping? FindMapping(Type type)
    {
        if (_typeMappings.TryGetValue(type, out var mapping))
        {
            return mapping;
        }
        return base.FindMapping(type);
    }

    /// <inheritdoc />
    public override RelationalTypeMapping? FindMapping(MemberInfo member)
    {
        return FindMapping(GetMemberType(member));
    }

    /// <summary>
    /// Gets the CLR type from a MemberInfo (property or field).
    /// </summary>
    private static Type GetMemberType(MemberInfo member)
    {
        return member switch
        {
            PropertyInfo propertyInfo => propertyInfo.PropertyType,
            FieldInfo fieldInfo => fieldInfo.FieldType,
            _ => throw new ArgumentException($"Unsupported member type: {member.GetType()}", nameof(member))
        };
    }

    /// <inheritdoc />
    public override RelationalTypeMapping? FindMapping(Type type, IModel model, CoreTypeMapping? elementMapping = null)
    {
        return FindMapping(type);
    }

    /// <inheritdoc />
    public override RelationalTypeMapping? FindMapping(MemberInfo member, IModel model, bool useAttributes)
    {
        return FindMapping(member);
    }
}
