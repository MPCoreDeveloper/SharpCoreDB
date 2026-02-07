using Microsoft.EntityFrameworkCore.Query;

namespace SharpCoreDB.EntityFrameworkCore.Query;

/// <summary>
/// Plugin that registers SharpCoreDB-specific member translators with the EF Core query pipeline.
/// Enables member accesses like <c>DateTime.Now</c>, <c>DateTime.UtcNow</c>, and <c>string.Length</c>
/// to translate into SharpCoreDB SQL expressions.
/// </summary>
public class SharpCoreDBMemberTranslatorPlugin(ISqlExpressionFactory sqlExpressionFactory) : IMemberTranslatorPlugin
{
    private readonly IMemberTranslator[] _translators =
    [
        new SharpCoreDBMemberTranslator(sqlExpressionFactory)
    ];

    /// <inheritdoc />
    public IEnumerable<IMemberTranslator> Translators => _translators;
}
