using Microsoft.EntityFrameworkCore.Storage;

namespace SharpCoreDB.EntityFrameworkCore.Storage;

/// <summary>
/// Type mapping source for SharpCoreDB.
/// Maps .NET types to SharpCoreDB SQL types (INTEGER, TEXT, REAL, DATETIME, etc.).
/// INCOMPLETE IMPLEMENTATION - Placeholder stub only.
/// </summary>
public class SharpCoreDBTypeMappingSource : IRelationalTypeMappingSource
{
    /// <inheritdoc />
    public RelationalTypeMapping? FindMapping(Type type)
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }

    /// <inheritdoc />
    public RelationalTypeMapping? FindMapping(string storeTypeName)
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }

    /// <inheritdoc />
    public RelationalTypeMapping? FindMapping(IProperty property)
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }

    /// <inheritdoc />
    public RelationalTypeMapping? FindMapping(IProperty property, IEntityType entityType)
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }

    /// <inheritdoc />
    public CoreTypeMapping? FindMapping(IElementType elementType)
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }
}
