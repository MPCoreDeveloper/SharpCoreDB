using System;
using LinqToDB.Mapping;

namespace SharpCoreDB.Functional.Linq2DB;

using SharpCoreDB;

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
        // ULID type mappings - store as TEXT string for portability and SQLite compatibility
        // This is critical for linq2db's SQLite provider to avoid "No mapping exists from object type SharpCoreDB.Ulid"
        // The converters must be registered BEFORE any queries; linq2db uses them for parameter binding in INSERT/UPDATE.
        // Note: The converter is registered on the MappingSchema, but for parameter binding in SQLite provider, it may require additional configuration in DataProvider or ValueConverter registration.
        SetConverter<Ulid, string>(ulid => ulid.ToString());
        SetConverter<string, Ulid>(str => Ulid.Parse(str));

        // Nullable ULID
        SetConverter<Ulid?, string?>(ulid => ulid?.ToString());
        SetConverter<string?, Ulid?>(str => string.IsNullOrWhiteSpace(str) ? null : Ulid.Parse(str));

        // GUIDs as string (N format for compactness)
        SetConverter<Guid, string>(guid => guid.ToString("N"));
        SetConverter<string, Guid>(str => Guid.Parse(str));

        // DateTime as ISO string for readability
        SetConverter<DateTime, string>(dt => dt.ToString("O"));
        SetConverter<string, DateTime>(str => DateTime.Parse(str));

        // DateTimeOffset
        SetConverter<DateTimeOffset, string>(dto => dto.ToString("O"));
        SetConverter<string, DateTimeOffset>(str => DateTimeOffset.Parse(str));

        // Boolean <-> integer for SQLite compatibility (avoids bool type issues in SQLite)
        SetConverter<bool, int>(b => b ? 1 : 0);
        SetConverter<int, bool>(i => i != 0);

        // The SetConverter calls above are sufficient for linq2db to map Ulid <-> string.
        // The SQLite provider will treat it as TEXT via the converter. No additional SetDataType is required
        // (DataType.NVarChar not available in current linq2db version; converters handle binding).

        // Binary as base64 string for TEXT columns, or keep byte[] as BLOB
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
