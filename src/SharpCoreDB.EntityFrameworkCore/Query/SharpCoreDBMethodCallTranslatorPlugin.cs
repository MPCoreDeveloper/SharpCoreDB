using Microsoft.EntityFrameworkCore.Query;

namespace SharpCoreDB.EntityFrameworkCore.Query;

/// <summary>
/// Plugin that registers SharpCoreDB-specific method call translators with the EF Core query pipeline.
/// Enables LINQ methods like <c>string.Contains()</c>, <c>StartsWith()</c>, <c>EF.Functions.Like()</c>
/// to translate into SharpCoreDB SQL expressions.
/// ✅ EF Core COLLATE Phase 6: Registered SharpCoreDBCollateTranslator for EF.Functions.Collate().
/// ✅ GraphRAG Phase 2: Registered GraphTraversalMethodCallTranslator for graph traversal queries.
/// </summary>
public class SharpCoreDBMethodCallTranslatorPlugin(ISqlExpressionFactory sqlExpressionFactory) : IMethodCallTranslatorPlugin
{
    private readonly IMethodCallTranslator[] _translators =
    [
        new SharpCoreDBStringMethodCallTranslator(sqlExpressionFactory),
        new SharpCoreDBCollateTranslator(sqlExpressionFactory),
        new GraphTraversalMethodCallTranslator(sqlExpressionFactory)
    ];

    /// <inheritdoc />
    public IEnumerable<IMethodCallTranslator> Translators => _translators;
}
