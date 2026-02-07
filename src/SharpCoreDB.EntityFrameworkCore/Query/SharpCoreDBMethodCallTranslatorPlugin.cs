using Microsoft.EntityFrameworkCore.Query;

namespace SharpCoreDB.EntityFrameworkCore.Query;

/// <summary>
/// Plugin that registers SharpCoreDB-specific method call translators with the EF Core query pipeline.
/// Enables LINQ methods like <c>string.Contains()</c>, <c>StartsWith()</c>, <c>EF.Functions.Like()</c>
/// to translate into SharpCoreDB SQL expressions.
/// </summary>
public class SharpCoreDBMethodCallTranslatorPlugin(ISqlExpressionFactory sqlExpressionFactory) : IMethodCallTranslatorPlugin
{
    private readonly IMethodCallTranslator[] _translators =
    [
        new SharpCoreDBStringMethodCallTranslator(sqlExpressionFactory)
    ];

    /// <inheritdoc />
    public IEnumerable<IMethodCallTranslator> Translators => _translators;
}
