namespace SharpCoreDB.Tests.Sql;

using Moq;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;

public sealed class AstExecutorVisitorTests
{
    [Theory]
    [MemberData(nameof(UnsupportedVisitorCases))]
    public void UnsupportedVisitors_ShouldThrowNotSupportedException(
        string expectedNodeType,
        Action action)
    {
        var exception = Assert.Throws<NotSupportedException>(action);

        Assert.Contains(expectedNodeType, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VisitSelect_WithSimpleFrom_ShouldReturnProjectedRows()
    {
        var table = new Mock<ITable>(MockBehavior.Strict);
        table.Setup(t => t.Select(null, null, true, false)).Returns([
            new Dictionary<string, object>
            {
                ["id"] = 1,
                ["name"] = "Alice"
            }
        ]);

        var tables = new Dictionary<string, ITable>(StringComparer.OrdinalIgnoreCase)
        {
            ["users"] = table.Object
        };

        var executor = new AstExecutor(tables, noEncrypt: false);
        var node = new SelectNode
        {
            From = new FromNode { TableName = "users" }
        };

        var rows = executor.VisitSelect(node);

        Assert.Single(rows);
        Assert.Equal(1, rows[0]["id"]);
        Assert.Equal("Alice", rows[0]["name"]);
        Assert.Equal(1, rows[0]["users.id"]);
        Assert.Equal("Alice", rows[0]["users.name"]);

        table.VerifyAll();
    }

    public static IEnumerable<object[]> UnsupportedVisitorCases()
    {
        yield return
        [
            nameof(InsertNode),
            (Action)(() => new AstExecutor([], noEncrypt: false).VisitInsert(new InsertNode()))
        ];

        yield return
        [
            nameof(UpdateNode),
            (Action)(() => new AstExecutor([], noEncrypt: false).VisitUpdate(new UpdateNode()))
        ];

        yield return
        [
            nameof(DeleteNode),
            (Action)(() => new AstExecutor([], noEncrypt: false).VisitDelete(new DeleteNode()))
        ];

        yield return
        [
            nameof(CreateTableNode),
            (Action)(() => new AstExecutor([], noEncrypt: false).VisitCreateTable(new CreateTableNode()))
        ];
    }
}
