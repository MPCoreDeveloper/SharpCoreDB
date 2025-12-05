using Microsoft.EntityFrameworkCore.Query;

namespace SharpCoreDB.EntityFrameworkCore.Query;

/// <summary>
/// Query SQL generator factory for SharpCoreDB.
/// Translates LINQ expressions to SharpCoreDB SQL syntax.
/// INCOMPLETE IMPLEMENTATION - Placeholder stub only.
/// </summary>
public class SharpCoreDBQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
{
    /// <inheritdoc />
    public QuerySqlGenerator Create()
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }
}
