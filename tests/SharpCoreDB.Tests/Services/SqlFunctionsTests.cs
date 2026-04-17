namespace SharpCoreDB.Tests.Sql;

using SharpCoreDB.Services;

public sealed class SqlFunctionsTests
{
    [Fact]
    public void Unhex_WithValidHex_ShouldDecodeUtf8Text()
    {
        // Arrange
        const string hex = "68656C6C6F"; // "hello"

        // Act
        var decoded = SqlFunctions.Unhex(hex);

        // Assert
        Assert.Equal("hello", decoded);
    }

    [Fact]
    public void EvaluateFunction_Unhex_ShouldDecodeText()
    {
        // Arrange
        List<object?> args = ["776F726C64"]; // "world"

        // Act
        var value = SqlFunctions.EvaluateFunction("UNHEX", args);

        // Assert
        Assert.Equal("world", value);
    }

    [Fact]
    public void Unhex_WithOddLength_ShouldThrowArgumentException()
    {
        // Arrange
        const string oddHex = "ABC";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => SqlFunctions.Unhex(oddHex));
    }

    [Fact]
    public void Quote_WithString_ShouldEscapeAndWrapInSingleQuotes()
    {
        // Arrange
        const string input = "O'Reilly";

        // Act
        var quoted = SqlFunctions.Quote(input);

        // Assert
        Assert.Equal("'O''Reilly'", quoted);
    }

    [Fact]
    public void Quote_WithNull_ShouldReturnNullLiteral()
    {
        // Act
        var quoted = SqlFunctions.Quote(null);

        // Assert
        Assert.Equal("NULL", quoted);
    }

    [Fact]
    public void EvaluateFunction_Quote_ShouldReturnSqlLiteral()
    {
        // Arrange
        List<object?> args = ["alpha"];

        // Act
        var value = SqlFunctions.EvaluateFunction("QUOTE", args);

        // Assert
        Assert.Equal("'alpha'", value);
    }

    [Fact]
    public void Char_WithAsciiCodePoints_ShouldBuildString()
    {
        // Act
        var value = SqlFunctions.Char(65, 66, 67);

        // Assert
        Assert.Equal("ABC", value);
    }

    [Fact]
    public void EvaluateFunction_Char_ShouldBuildString()
    {
        // Arrange
        List<object?> args = [72, 105];

        // Act
        var value = SqlFunctions.EvaluateFunction("CHAR", args);

        // Assert
        Assert.Equal("Hi", value);
    }

    [Fact]
    public void Unicode_WithSimpleCharacter_ShouldReturnCodePoint()
    {
        // Act
        var codePoint = SqlFunctions.Unicode("A");

        // Assert
        Assert.Equal(65, codePoint);
    }

    [Fact]
    public void Unicode_WithEmoji_ShouldReturnSupplementaryCodePoint()
    {
        // Arrange
        const string value = "😀";

        // Act
        var codePoint = SqlFunctions.Unicode(value);

        // Assert
        Assert.Equal(0x1F600, codePoint);
    }

    [Fact]
    public void EvaluateFunction_Unicode_ShouldReturnCodePoint()
    {
        // Arrange
        List<object?> args = ["Z"];

        // Act
        var value = SqlFunctions.EvaluateFunction("UNICODE", args);

        // Assert
        Assert.Equal(90, value);
    }
}
