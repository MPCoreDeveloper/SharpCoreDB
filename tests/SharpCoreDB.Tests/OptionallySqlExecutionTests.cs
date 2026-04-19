namespace SharpCoreDB.Tests;

using Moq;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;

public sealed class OptionallySqlExecutionTests
{
    private static List<Dictionary<string, object>> Execute(string sql, params (string name, Dictionary<string, object>[] rows)[] tables)
    {
        var tableDict = new Dictionary<string, ITable>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, rows) in tables)
        {
            var mock = new Mock<ITable>(MockBehavior.Strict);
            var rowList = rows.ToList();
            mock.Setup(t => t.Select(null, null, true, false)).Returns(rowList);
            tableDict[name] = mock.Object;
        }

        var parser = new EnhancedSqlParser();
        var node = parser.Parse(sql) as SelectNode
            ?? throw new InvalidOperationException("Expected SELECT AST node");

        var executor = new AstExecutor(tableDict, noEncrypt: false);
        return executor.ExecuteSelect(node);
    }

    [Fact]
    public void ExecuteSelect_WithIsSomePredicate_ShouldFilterNonNullRows()
    {
        // Arrange/Act
        var rows = Execute(
            "SELECT id FROM users WHERE email IS SOME",
            ("users", [
                new() { ["id"] = 1, ["email"] = "a@b.com" },
                new() { ["id"] = 2, ["email"] = DBNull.Value },
                new() { ["id"] = 3, ["email"] = "x@y.com" }
            ]));

        // Assert
        Assert.Equal(2, rows.Count);
        Assert.Equal("1", rows[0]["id"].ToString());
        Assert.Equal("3", rows[1]["id"].ToString());
    }

    [Fact]
    public void ExecuteSelect_WithIsNonePredicate_ShouldFilterNullRows()
    {
        // Arrange/Act
        var rows = Execute(
            "SELECT id FROM users WHERE email IS NONE",
            ("users", [
                new() { ["id"] = 1, ["email"] = "a@b.com" },
                new() { ["id"] = 2, ["email"] = DBNull.Value },
                new() { ["id"] = 3, ["email"] = DBNull.Value }
            ]));

        // Assert
        Assert.Equal(2, rows.Count);
        Assert.Equal("2", rows[0]["id"].ToString());
        Assert.Equal("3", rows[1]["id"].ToString());
    }
}
