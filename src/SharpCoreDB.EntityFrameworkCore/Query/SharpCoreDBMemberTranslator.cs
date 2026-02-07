using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Reflection;

namespace SharpCoreDB.EntityFrameworkCore.Query;

/// <summary>
/// Translates member accesses to SharpCoreDB SQL expressions.
/// Supports <see cref="DateTime.Now"/>, <see cref="DateTime.UtcNow"/>, and <see cref="string.Length"/>.
/// </summary>
public class SharpCoreDBMemberTranslator(ISqlExpressionFactory sqlExpressionFactory) : IMemberTranslator
{
    private readonly ISqlExpressionFactory _sqlExpressionFactory = sqlExpressionFactory
        ?? throw new ArgumentNullException(nameof(sqlExpressionFactory));

    /// <inheritdoc />
    public SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        // DateTime.Now => NOW()
        if (member.DeclaringType == typeof(DateTime) && member.Name == nameof(DateTime.Now))
        {
            return _sqlExpressionFactory.Function(
                "NOW", [], nullable: false, argumentsPropagateNullability: [],
                typeof(DateTime));
        }

        // DateTime.UtcNow => NOW()
        if (member.DeclaringType == typeof(DateTime) && member.Name == nameof(DateTime.UtcNow))
        {
            return _sqlExpressionFactory.Function(
                "NOW", [], nullable: false, argumentsPropagateNullability: [],
                typeof(DateTime));
        }

        // string.Length => LENGTH(column)
        if (member.DeclaringType == typeof(string) && member.Name == nameof(string.Length) && instance is not null)
        {
            return _sqlExpressionFactory.Function(
                "LENGTH", [instance], nullable: true, argumentsPropagateNullability: [true],
                typeof(int));
        }

        return null;
    }
}
