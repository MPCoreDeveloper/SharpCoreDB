using LinqToDB.Mapping;

namespace SharpCoreDB.Functional.Linq2DB;

/// <summary>
/// Custom mapping schema for SharpCoreDB type mappings.
/// Handles ULID, GUID, and other specialized types.
/// </summary>
public sealed class SharpCoreDBMappingSchema : MappingSchema
{
    /// <summary>
    /// Gets the singleton instance of the SharpCoreDB mapping schema.
    /// </summary>
    public static readonly SharpCoreDBMappingSchema Instance = new();

    private SharpCoreDBMappingSchema() : base("SharpCoreDB")
    {
        // ULID type mappings - store as string for portability
        SetConverter<Ulid, string>(ulid => ulid.ToString());
        SetConverter<string, Ulid>(str => Ulid.Parse(str));

        // Nullable ULID
        SetConverter<Ulid?, string?>(ulid => ulid?.ToString());
        SetConverter<string?, Ulid?>(str => string.IsNullOrWhiteSpace(str) ? null : Ulid.Parse(str));

        // GUIDs as string (N format for compactness)
        SetConverter<Guid, string>(guid => guid.ToString("N"));
        SetConverter<string, Guid>(str => Guid.Parse(str));

        // DateTime as ISO string for readability (or long ticks)
        SetConverter<DateTime, string>(dt => dt.ToString("O"));
        SetConverter<string, DateTime>(str => DateTime.Parse(str));

        // DateTimeOffset
        SetConverter<DateTimeOffset, string>(dto => dto.ToString("O"));
        SetConverter<string, DateTimeOffset>(str => DateTimeOffset.Parse(str));

        // Boolean <-> integer for SQLite compatibility
        SetConverter<bool, int>(b => b ? 1 : 0);
        SetConverter<int, bool>(i => i != 0);

        // Binary as base64 string for TEXT columns, or keep BLOB via byte[]
        // Default keeps byte[] as BLOB which SQLite provider handles well
    }

    /// <summary>
    /// Configures a custom type mapping.
    /// </summary>
    /// <typeparam name="TSource">Source type</typeparam>
    /// <typeparam name="TTarget">Target type</typeparam>
    /// <param name="toTarget">Conversion function to target</param>
    /// <param name="toSource">Conversion function to source</param>
    public void ConfigureCustomMapping<TSource, TTarget>(
        Func<TSource, TTarget> toTarget,
        Func<TTarget, TSource> toSource)
    {
        SetConverter(toTarget);
        SetConverter(toSource);
    }
}
