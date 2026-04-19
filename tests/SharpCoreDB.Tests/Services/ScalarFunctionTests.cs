namespace SharpCoreDB.Tests.Sql;

using Moq;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;

/// <summary>
/// Tests for scalar function support in the AstExecutor (Quick Wins implementation).
/// Covers: COALESCE, IFNULL, NULLIF, IIF, TYPEOF, ABS, ROUND, CEIL, FLOOR,
///         UPPER, LOWER, LENGTH, TRIM, LTRIM, RTRIM, SUBSTR, REPLACE, INSTR,
///         REGEXP operator, multi-column ORDER BY.
/// </summary>
public sealed class ScalarFunctionTests
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
        var node = parser.Parse(sql) as SelectNode
            ?? throw new InvalidOperationException($"Failed to parse: {sql}");
        return executor.ExecuteSelect(node);
    }

    // ── NULL-safety ──────────────────────────────────────────────────────────

    [Fact]
    public void Coalesce_WithFirstNonNull_ReturnsFirstValue()
    {
        var rows = Execute(
            "SELECT COALESCE(name, 'unknown') AS n FROM t",
            ("t", [new() { ["name"] = "Alice" }]));

        Assert.Equal("Alice", rows[0]["n"]);
    }

    [Fact]
    public void Coalesce_WithNullFirstArg_ReturnsFallback()
    {
        var rows = Execute(
            "SELECT COALESCE(name, 'unknown') AS n FROM t",
            ("t", [new() { ["name"] = DBNull.Value }]));

        Assert.Equal("unknown", rows[0]["n"]);
    }

    [Fact]
    public void Ifnull_WithNonNullValue_ReturnsOriginal()
    {
        var rows = Execute(
            "SELECT IFNULL(score, 0) AS s FROM t",
            ("t", [new() { ["score"] = 42 }]));

        Assert.Equal(42, rows[0]["s"]);
    }

    [Fact]
    public void Ifnull_WithNullValue_ReturnsFallback()
    {
        var rows = Execute(
            "SELECT IFNULL(score, 0) AS s FROM t",
            ("t", [new() { ["score"] = DBNull.Value }]));

        Assert.Equal(0, rows[0]["s"]);
    }

    [Fact]
    public void Nullif_WhenValuesEqual_ReturnsNull()
    {
        var rows = Execute(
            "SELECT NULLIF(status, 'inactive') AS s FROM t",
            ("t", [new() { ["status"] = "inactive" }]));

        Assert.Null(rows[0]["s"]);
    }

    [Fact]
    public void Nullif_WhenValuesDiffer_ReturnsFirstArg()
    {
        var rows = Execute(
            "SELECT NULLIF(status, 'inactive') AS s FROM t",
            ("t", [new() { ["status"] = "active" }]));

        Assert.Equal("active", rows[0]["s"]);
    }

    [Fact]
    public void Iif_WhenConditionTrue_ReturnsTrueArm()
    {
        var rows = Execute(
            "SELECT IIF(active, 'yes', 'no') AS v FROM t",
            ("t", [new() { ["active"] = true }]));

        Assert.Equal("yes", rows[0]["v"]);
    }

    [Fact]
    public void Iif_WhenConditionFalse_ReturnsFalseArm()
    {
        var rows = Execute(
            "SELECT IIF(active, 'yes', 'no') AS v FROM t",
            ("t", [new() { ["active"] = false }]));

        Assert.Equal("no", rows[0]["v"]);
    }

    [Theory]
    [InlineData(42, "integer")]
    [InlineData("hello", "text")]
    [InlineData(3.14, "real")]
    public void Typeof_ReturnsCorrectSqliteType(object columnValue, string expectedType)
    {
        var rows = Execute(
            "SELECT TYPEOF(val) AS t FROM t",
            ("t", [new() { ["val"] = columnValue }]));

        Assert.Equal(expectedType, rows[0]["t"]);
    }

    // ── Numeric functions ────────────────────────────────────────────────────

    [Theory]
    [InlineData(-5, 5)]
    [InlineData(3, 3)]
    [InlineData(-3.7, 3.7)]
    public void Abs_ReturnsAbsoluteValue(object input, double expected)
    {
        var rows = Execute(
            "SELECT ABS(val) AS v FROM t",
            ("t", [new() { ["val"] = input }]));

        Assert.Equal(Convert.ToDouble(expected), Convert.ToDouble(rows[0]["v"]), precision: 10);
    }

    [Theory]
    [InlineData(3.456, 2, 3.46)]
    [InlineData(3.456, 0, 3.0)]
    [InlineData(2.5, 0, 3.0)]
    public void Round_ReturnsRoundedValue(double input, int digits, double expected)
    {
        var rows = Execute(
            $"SELECT ROUND(val, {digits}) AS v FROM t",
            ("t", [new() { ["val"] = input }]));

        Assert.Equal(expected, Convert.ToDouble(rows[0]["v"]), precision: 10);
    }

    [Theory]
    [InlineData(2.1, 3.0)]
    [InlineData(3.0, 3.0)]
    [InlineData(-2.1, -2.0)]
    public void Ceil_ReturnsCeilingValue(double input, double expected)
    {
        var rows = Execute(
            "SELECT CEIL(val) AS v FROM t",
            ("t", [new() { ["val"] = input }]));

        Assert.Equal(expected, Convert.ToDouble(rows[0]["v"]), precision: 10);
    }

    [Theory]
    [InlineData(2.9, 2.0)]
    [InlineData(3.0, 3.0)]
    [InlineData(-2.1, -3.0)]
    public void Floor_ReturnsFloorValue(double input, double expected)
    {
        var rows = Execute(
            "SELECT FLOOR(val) AS v FROM t",
            ("t", [new() { ["val"] = input }]));

        Assert.Equal(expected, Convert.ToDouble(rows[0]["v"]), precision: 10);
    }

    // ── String functions ─────────────────────────────────────────────────────

    [Fact]
    public void Upper_ConvertsToUppercase()
    {
        var rows = Execute(
            "SELECT UPPER(name) AS n FROM t",
            ("t", [new() { ["name"] = "hello" }]));

        Assert.Equal("HELLO", rows[0]["n"]);
    }

    [Fact]
    public void Lower_ConvertsToLowercase()
    {
        var rows = Execute(
            "SELECT LOWER(name) AS n FROM t",
            ("t", [new() { ["name"] = "WORLD" }]));

        Assert.Equal("world", rows[0]["n"]);
    }

    [Fact]
    public void Length_ReturnsStringLength()
    {
        var rows = Execute(
            "SELECT LENGTH(name) AS l FROM t",
            ("t", [new() { ["name"] = "Alice" }]));

        Assert.Equal(5, rows[0]["l"]);
    }

    [Fact]
    public void Trim_RemovesLeadingAndTrailingSpaces()
    {
        var rows = Execute(
            "SELECT TRIM(name) AS n FROM t",
            ("t", [new() { ["name"] = "  hello  " }]));

        Assert.Equal("hello", rows[0]["n"]);
    }

    [Fact]
    public void Ltrim_RemovesLeadingSpaces()
    {
        var rows = Execute(
            "SELECT LTRIM(name) AS n FROM t",
            ("t", [new() { ["name"] = "  hello  " }]));

        Assert.Equal("hello  ", rows[0]["n"]);
    }

    [Fact]
    public void Rtrim_RemovesTrailingSpaces()
    {
        var rows = Execute(
            "SELECT RTRIM(name) AS n FROM t",
            ("t", [new() { ["name"] = "  hello  " }]));

        Assert.Equal("  hello", rows[0]["n"]);
    }

    [Fact]
    public void Substr_WithStartAndLength_ReturnsSubstring()
    {
        var rows = Execute(
            "SELECT SUBSTR(name, 2, 3) AS s FROM t",
            ("t", [new() { ["name"] = "hello" }]));

        Assert.Equal("ell", rows[0]["s"]);
    }

    [Fact]
    public void Substr_WithStartOnly_ReturnsRestOfString()
    {
        var rows = Execute(
            "SELECT SUBSTR(name, 3) AS s FROM t",
            ("t", [new() { ["name"] = "hello" }]));

        Assert.Equal("llo", rows[0]["s"]);
    }

    [Fact]
    public void Replace_SubstitutesSubstring()
    {
        var rows = Execute(
            "SELECT REPLACE(name, 'o', '0') AS n FROM t",
            ("t", [new() { ["name"] = "hello world" }]));

        Assert.Equal("hell0 w0rld", rows[0]["n"]);
    }

    [Fact]
    public void Instr_ReturnsOneBasedPosition()
    {
        var rows = Execute(
            "SELECT INSTR(name, 'lo') AS pos FROM t",
            ("t", [new() { ["name"] = "hello" }]));

        Assert.Equal(4, rows[0]["pos"]);
    }

    [Fact]
    public void Instr_WhenNotFound_ReturnsZero()
    {
        var rows = Execute(
            "SELECT INSTR(name, 'xyz') AS pos FROM t",
            ("t", [new() { ["name"] = "hello" }]));

        Assert.Equal(0, rows[0]["pos"]);
    }

    // ── REGEXP operator ──────────────────────────────────────────────────────

    [Fact]
    public void Regexp_WhenPatternMatches_ReturnsRow()
    {
        var rows = Execute(
            "SELECT name FROM t WHERE name REGEXP '^Al'",
            ("t", [
                new() { ["name"] = "Alice" },
                new() { ["name"] = "Bob" }
            ]));

        Assert.Single(rows);
        Assert.Equal("Alice", rows[0]["name"]);
    }

    [Fact]
    public void Regexp_WhenNoPatternMatch_ReturnsEmpty()
    {
        var rows = Execute(
            "SELECT name FROM t WHERE name REGEXP '^Z'",
            ("t", [
                new() { ["name"] = "Alice" },
                new() { ["name"] = "Bob" }
            ]));

        Assert.Empty(rows);
    }

    // ── Multi-column ORDER BY ────────────────────────────────────────────────

    [Fact]
    public void OrderBy_MultipleColumns_SortsCorrectly()
    {
        var rows = Execute(
            "SELECT dept, name FROM t ORDER BY dept ASC, name ASC",
            ("t", [
                new() { ["dept"] = "B", ["name"] = "Zoe" },
                new() { ["dept"] = "A", ["name"] = "Bob" },
                new() { ["dept"] = "A", ["name"] = "Alice" },
                new() { ["dept"] = "B", ["name"] = "Ann" },
            ]));

        Assert.Equal(4, rows.Count);
        Assert.Equal("Alice", rows[0]["name"]);
        Assert.Equal("Bob", rows[1]["name"]);
        Assert.Equal("Ann", rows[2]["name"]);
        Assert.Equal("Zoe", rows[3]["name"]);
    }

    [Fact]
    public void OrderBy_MultipleColumns_SecondaryDescending_SortsCorrectly()
    {
        var rows = Execute(
            "SELECT dept, name FROM t ORDER BY dept ASC, name DESC",
            ("t", [
                new() { ["dept"] = "A", ["name"] = "Alice" },
                new() { ["dept"] = "A", ["name"] = "Bob" },
                new() { ["dept"] = "A", ["name"] = "Charlie" },
            ]));

        Assert.Equal("Charlie", rows[0]["name"]);
        Assert.Equal("Bob", rows[1]["name"]);
        Assert.Equal("Alice", rows[2]["name"]);
    }

    // ── W2-3: Extended numeric scalar functions ──────────────────────────────

    [Theory]
    [InlineData(2, 3, 8.0)]
    [InlineData(9, 0.5, 3.0)]
    public void Pow_ReturnsExponent(object x, object y, double expected)
    {
        var rows = Execute(
            $"SELECT POW(val, exp) AS v FROM t",
            ("t", [new() { ["val"] = x, ["exp"] = y }]));

        Assert.Equal(expected, Convert.ToDouble(rows[0]["v"]), precision: 10);
    }

    [Fact]
    public void Power_AliasWorks()
    {
        var rows = Execute(
            "SELECT POWER(val, 2) AS v FROM t",
            ("t", [new() { ["val"] = 5 }]));

        Assert.Equal(25.0, Convert.ToDouble(rows[0]["v"]), precision: 10);
    }

    [Theory]
    [InlineData(16, 4.0)]
    [InlineData(2, 1.4142135623730951)]
    public void Sqrt_ReturnsSquareRoot(object input, double expected)
    {
        var rows = Execute(
            "SELECT SQRT(val) AS v FROM t",
            ("t", [new() { ["val"] = input }]));

        Assert.Equal(expected, Convert.ToDouble(rows[0]["v"]), precision: 10);
    }

    [Theory]
    [InlineData(10, 3, 1.0)]
    [InlineData(7, 2, 1.0)]
    public void Mod_ReturnsModulo(object x, object y, double expected)
    {
        var rows = Execute(
            "SELECT MOD(a, b) AS v FROM t",
            ("t", [new() { ["a"] = x, ["b"] = y }]));

        Assert.Equal(expected, Convert.ToDouble(rows[0]["v"]), precision: 10);
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(-3, -1)]
    [InlineData(0, 0)]
    public void Sign_ReturnsSignOfValue(object input, int expected)
    {
        var rows = Execute(
            "SELECT SIGN(val) AS v FROM t",
            ("t", [new() { ["val"] = input }]));

        Assert.Equal(expected, Convert.ToInt32(rows[0]["v"]));
    }

    [Fact]
    public void Random_ReturnsInt64()
    {
        var rows = Execute(
            "SELECT RANDOM() AS v FROM t",
            ("t", [new() { ["x"] = 1 }]));

        Assert.IsType<long>(rows[0]["v"]);
    }

    [Theory]
    [InlineData(3, 7, 7)]
    [InlineData(10, 2, 10)]
    public void ScalarMax_ReturnsBiggerValue(object a, object b, int expected)
    {
        var rows = Execute(
            "SELECT MAX(x, y) AS v FROM t",
            ("t", [new() { ["x"] = a, ["y"] = b }]));

        Assert.Equal(expected, Convert.ToInt32(rows[0]["v"]));
    }

    [Theory]
    [InlineData(3, 7, 3)]
    [InlineData(10, 2, 2)]
    public void ScalarMin_ReturnsSmallerValue(object a, object b, int expected)
    {
        var rows = Execute(
            "SELECT MIN(x, y) AS v FROM t",
            ("t", [new() { ["x"] = a, ["y"] = b }]));

        Assert.Equal(expected, Convert.ToInt32(rows[0]["v"]));
    }

    // ── W2-4: GLOB operator ─────────────────────────────────────────────────

    [Fact]
    public void Glob_StarWildcard_MatchesAnySequence()
    {
        var rows = Execute(
            "SELECT name FROM t WHERE name GLOB 'Al*'",
            ("t", [
                new() { ["name"] = "Alice" },
                new() { ["name"] = "Bob" },
                new() { ["name"] = "Alex" },
            ]));

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Glob_QuestionWildcard_MatchesSingleChar()
    {
        var rows = Execute(
            "SELECT name FROM t WHERE name GLOB '?ob'",
            ("t", [
                new() { ["name"] = "Bob" },
                new() { ["name"] = "Rob" },
                new() { ["name"] = "Bobby" },
            ]));

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Glob_CharacterClass_MatchesRange()
    {
        var rows = Execute(
            "SELECT name FROM t WHERE name GLOB '[A-C]*'",
            ("t", [
                new() { ["name"] = "Alice" },
                new() { ["name"] = "Bob" },
                new() { ["name"] = "Dave" },
            ]));

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Glob_IsCaseSensitive()
    {
        var rows = Execute(
            "SELECT name FROM t WHERE name GLOB 'a*'",
            ("t", [
                new() { ["name"] = "Alice" },
                new() { ["name"] = "alice" },
            ]));

        Assert.Single(rows);
        Assert.Equal("alice", rows[0]["name"]);
    }

    [Fact]
    public void NotGlob_ExcludesMatchingRows()
    {
        var rows = Execute(
            "SELECT name FROM t WHERE name NOT GLOB 'A*'",
            ("t", [
                new() { ["name"] = "Alice" },
                new() { ["name"] = "Bob" },
            ]));

        Assert.Single(rows);
        Assert.Equal("Bob", rows[0]["name"]);
    }
}
