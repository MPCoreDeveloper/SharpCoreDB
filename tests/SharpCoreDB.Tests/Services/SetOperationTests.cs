namespace SharpCoreDB.Tests.Sql;

using Moq;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;

/// <summary>
/// Tests for set operations (UNION / UNION ALL / INTERSECT / EXCEPT) — W2-1.
/// </summary>
public sealed class SetOperationTests
{
    private static AstExecutor CreateExecutor(params (string name, Dictionary<string, object>[] rows)[] tables)
    {
        var tableDict = new Dictionary<string, ITable>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, rows) in tables)
        {
            var mock = new Mock<ITable>(MockBehavior.Strict);
            var rowList = rows.ToList();
            mock.Setup(t => t.Select(null, null, true, false)).Returns(rowList);
            tableDict[name] = mock.Object;
        }
        return new AstExecutor(tableDict, noEncrypt: false);
    }

    private static List<Dictionary<string, object>> Execute(string sql, params (string, Dictionary<string, object>[])[] tables)
    {
        var executor = CreateExecutor(tables);
        var parser = new EnhancedSqlParser();
        var parsed = parser.Parse(sql);
        return parsed switch
        {
            SetOperationNode setOp => executor.ExecuteSetOperation(setOp),
            SelectNode select => executor.ExecuteSelect(select),
            _ => throw new InvalidOperationException($"Unexpected node type: {parsed?.GetType().Name}"),
        };
    }

    // ── UNION ALL ────────────────────────────────────────────────────────────

    [Fact]
    public void UnionAll_CombinesAllRows_IncludingDuplicates()
    {
        var rows = Execute(
            "SELECT name FROM a UNION ALL SELECT name FROM b",
            ("a", [new() { ["name"] = "Alice" }, new() { ["name"] = "Bob" }]),
            ("b", [new() { ["name"] = "Bob" }, new() { ["name"] = "Charlie" }]));

        Assert.Equal(4, rows.Count);
    }

    // ── UNION ────────────────────────────────────────────────────────────────

    [Fact]
    public void Union_DeduplicatesRows()
    {
        var rows = Execute(
            "SELECT name FROM a UNION SELECT name FROM b",
            ("a", [new() { ["name"] = "Alice" }, new() { ["name"] = "Bob" }]),
            ("b", [new() { ["name"] = "Bob" }, new() { ["name"] = "Charlie" }]));

        Assert.Equal(3, rows.Count);
    }

    // ── INTERSECT ────────────────────────────────────────────────────────────

    [Fact]
    public void Intersect_ReturnsCommonRows()
    {
        var rows = Execute(
            "SELECT name FROM a INTERSECT SELECT name FROM b",
            ("a", [new() { ["name"] = "Alice" }, new() { ["name"] = "Bob" }]),
            ("b", [new() { ["name"] = "Bob" }, new() { ["name"] = "Charlie" }]));

        Assert.Single(rows);
        Assert.Equal("Bob", rows[0]["name"]);
    }

    // ── EXCEPT ───────────────────────────────────────────────────────────────

    [Fact]
    public void Except_ReturnsRowsOnlyInLeft()
    {
        var rows = Execute(
            "SELECT name FROM a EXCEPT SELECT name FROM b",
            ("a", [new() { ["name"] = "Alice" }, new() { ["name"] = "Bob" }]),
            ("b", [new() { ["name"] = "Bob" }, new() { ["name"] = "Charlie" }]));

        Assert.Single(rows);
        Assert.Equal("Alice", rows[0]["name"]);
    }

    // ── ORDER BY on set result ───────────────────────────────────────────────

    [Fact]
    public void Union_WithOrderBy_SortsCombinedResult()
    {
        var rows = Execute(
            "SELECT name FROM a UNION ALL SELECT name FROM b ORDER BY name ASC",
            ("a", [new() { ["name"] = "Charlie" }]),
            ("b", [new() { ["name"] = "Alice" }]));

        Assert.Equal(2, rows.Count);
        Assert.Equal("Alice", rows[0]["name"]);
        Assert.Equal("Charlie", rows[1]["name"]);
    }

    // ── LIMIT on set result ──────────────────────────────────────────────────

    [Fact]
    public void UnionAll_WithLimit_TruncatesResult()
    {
        var rows = Execute(
            "SELECT name FROM a UNION ALL SELECT name FROM b LIMIT 2",
            ("a", [new() { ["name"] = "Alice" }, new() { ["name"] = "Bob" }]),
            ("b", [new() { ["name"] = "Charlie" }]));

        Assert.Equal(2, rows.Count);
    }

    // ── Parser round-trip ────────────────────────────────────────────────────

    [Fact]
    public void Parser_RecognizesUnionAll()
    {
        var parser = new EnhancedSqlParser();
        var node = parser.Parse("SELECT a FROM t1 UNION ALL SELECT a FROM t2");

        Assert.IsType<SetOperationNode>(node);
        var set = (SetOperationNode)node;
        Assert.Equal(SetOperationType.UnionAll, set.Operation);
    }

    [Fact]
    public void Parser_RecognizesIntersect()
    {
        var parser = new EnhancedSqlParser();
        var node = parser.Parse("SELECT a FROM t1 INTERSECT SELECT a FROM t2");

        Assert.IsType<SetOperationNode>(node);
        var set = (SetOperationNode)node;
        Assert.Equal(SetOperationType.Intersect, set.Operation);
    }

    [Fact]
    public void Parser_RecognizesExcept()
    {
        var parser = new EnhancedSqlParser();
        var node = parser.Parse("SELECT a FROM t1 EXCEPT SELECT a FROM t2");

        Assert.IsType<SetOperationNode>(node);
        var set = (SetOperationNode)node;
        Assert.Equal(SetOperationType.Except, set.Operation);
    }

    [Fact]
    public void Parser_PlainSelect_RemainsSelectNode()
    {
        var parser = new EnhancedSqlParser();
        var node = parser.Parse("SELECT a FROM t1");

        Assert.IsType<SelectNode>(node);
    }
}
