using Moq;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;

namespace SharpCoreDB.Tests.QueryExecution;

public sealed class CompiledQueryExecutorTests
{
    [Fact]
    public void Execute_WithIndexedProjectionNullValue_ShouldProjectDBNull()
    {
        // Arrange
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["Name"] = "Alice" }
        };

        var tableMock = new Mock<ITable>();
        tableMock.Setup(t => t.Select(null, null, true)).Returns(rows);

        var tables = new Dictionary<string, ITable>(StringComparer.OrdinalIgnoreCase)
        {
            ["Users"] = tableMock.Object,
        };

        var executor = new CompiledQueryExecutor(tables);

        var plan = new CompiledQueryPlan(
            sql: "SELECT Missing FROM Users",
            tableName: "Users",
            selectColumns: ["Missing"],
            isSelectAll: false,
            whereFilter: null,
            whereFilterIndexed: null,
            projectionFunc: static row => new Dictionary<string, object>(row),
            orderByColumn: null,
            orderByAscending: true,
            limit: null,
            offset: null,
            parameterNames: [],
            optimizedPlan: null,
            optimizedCost: 0,
            columnIndices: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Name"] = 0, ["Missing"] = 1 },
            useDirectColumnAccess: true);

        // Act
        var result = executor.Execute(plan);

        // Assert
        Assert.Single(result);
        Assert.True(result[0].ContainsKey("Missing"));
        Assert.Equal(DBNull.Value, result[0]["Missing"]);
    }
}
