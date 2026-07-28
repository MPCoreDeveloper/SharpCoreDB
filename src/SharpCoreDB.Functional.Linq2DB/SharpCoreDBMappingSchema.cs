using System;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;

namespace SharpCoreDB.Functional.Linq2DB;

using SharpCoreDB;
using DataType = LinqToDB.DataType;

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
        // === ULID Value Converter (critical for SQLite/linq2db parameter binding) ===
        // Simple SetConverter<string, Ulid> is insufficient for INSERT/parameter binding.
        // We register CLR → DataParameter (specifies DataType = NVarChar so SQLite provider knows how to bind as TEXT).
        // DB → CLR uses the direct converter.
        // This is the standard pattern from linq2db docs/discussions for custom scalar types like ULID.

        // Ulid → DB parameter (string stored as TEXT/NVarChar)
        // Use DataType.NVarChar (standard for string parameters in linq2db; SQLite maps it to TEXT affinity)
        SetConverter<Ulid, DataParameter>(ulid =>
            new DataParameter(null, ulid.ToString(), DataType.NVarChar));

        // string → Ulid (for SELECT/materialization)
        SetConverter<string, Ulid>(str => Ulid.Parse(str));

        // Nullable ULID support (fixed: use `ulid != null` instead of method group `.HasValue`)
        SetConverter<Ulid?, DataParameter>(ulid =>
            ulid != null
                ? new DataParameter(null, ulid.Value.ToString(), DataType.NVarChar)
                : new DataParameter(null, DBNull.Value, DataType.NVarChar));
        SetConverter<string?, Ulid?>(str => string.IsNullOrWhiteSpace(str) ? null : Ulid.Parse(str));

        // GUIDs as compact string
        SetConverter<Guid, string>(guid => guid.ToString("N"));
        SetConverter<string, Guid>(str => Guid.Parse(str));

        // DateTime as ISO string
        SetConverter<DateTime, string>(dt => dt.ToString("O"));
        SetConverter<string, DateTime>(str => DateTime.Parse(str));

        // DateTimeOffset
        SetConverter<DateTimeOffset, string>(dto => dto.ToString("O"));
        SetConverter<string, DateTimeOffset>(str => DateTimeOffset.Parse(str));

        // Boolean <-> integer for SQLite compatibility
        SetConverter<bool, int>(b => b ? 1 : 0);
        SetConverter<int, bool>(i => i != 0);

        // Binary remains as byte[] (BLOB) — handled natively by SQLite provider

        // Register as scalar type for additional provider hints
        // (AddScalarType helps the provider recognize the type; NVarChar maps to TEXT in SQLite provider)
        AddScalarType(typeof(Ulid), DataType.NVarChar);
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
