using Microsoft.EntityFrameworkCore.Query;

namespace SharpCoreDB.EntityFrameworkCore.Query;

/// <summary>
/// Query SQL generator factory for SharpCoreDB.
/// Creates query SQL generators that translate LINQ expressions to SharpCoreDB SQL syntax.
/// </summary>
public class SharpCoreDBQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
{
    private readonly QuerySqlGeneratorDependencies _dependencies;

    /// <summary>
    /// Initializes a new instance of the SharpCoreDBQuerySqlGeneratorFactory class.
    /// </summary>
    public SharpCoreDBQuerySqlGeneratorFactory(QuerySqlGeneratorDependencies dependencies)
    {
        _dependencies = dependencies;
    }

    /// <inheritdoc />
    public QuerySqlGenerator Create()
    {
        return new SharpCoreDBQuerySqlGenerator(_dependencies);
    }
}
