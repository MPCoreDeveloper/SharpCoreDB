using Microsoft.EntityFrameworkCore.Query;

namespace SharpCoreDB.EntityFrameworkCore.Query;

/// <summary>
/// Factory for SharpCoreDB query translation preprocessors.
/// </summary>
public sealed class SharpCoreDBQueryTranslationPreprocessorFactory(
    QueryTranslationPreprocessorDependencies dependencies,
    RelationalQueryTranslationPreprocessorDependencies relationalDependencies)
    : IQueryTranslationPreprocessorFactory
{
    /// <inheritdoc />
    public QueryTranslationPreprocessor Create(QueryCompilationContext queryCompilationContext)
        => new SharpCoreDBQueryTranslationPreprocessor(
            dependencies,
            relationalDependencies,
            queryCompilationContext);
}
