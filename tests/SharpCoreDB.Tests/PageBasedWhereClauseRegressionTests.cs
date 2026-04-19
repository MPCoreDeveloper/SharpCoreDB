namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Regression tests for PageBased WHERE clause evaluation with mixed predicates.
/// </summary>
public class PageBasedWhereClauseRegressionTests
{
    [Fact]
    public void ExecuteQuery_WithAndAndLessThanOrEqual_OnPageBasedTable_ReturnsMatchingRows()
    {
        // Arrange
        var database = CreateDatabase();
        database.ExecuteSQL("CREATE TABLE snapshots (stream_id TEXT, version LONG, payload TEXT) STORAGE = PAGE_BASED");
        database.ExecuteBatchSQL([
            "INSERT INTO snapshots VALUES ('order-filter', 10, 'v10')",
            "INSERT INTO snapshots VALUES ('order-filter', 30, 'v30')",
            "INSERT INTO snapshots VALUES ('other-stream', 15, 'x15')"]);
        database.Flush();
        database.ForceSave();

        // Act
        var rows = database.ExecuteQuery(
            "SELECT stream_id, version, payload FROM snapshots WHERE stream_id = ? AND version <= 20 ORDER BY version DESC LIMIT 1",
            new Dictionary<string, object?> { ["0"] = "order-filter" });

        // Assert
        Assert.Single(rows);
        Assert.Equal("order-filter", rows[0]["stream_id"]);
        Assert.Equal(10L, Convert.ToInt64(rows[0]["version"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("v10", rows[0]["payload"]);
    }

    private static SharpCoreDB.Interfaces.IDatabase CreateDatabase()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<DatabaseFactory>();
        var path = Path.Combine(Path.GetTempPath(), $"SharpCoreDB_PageBasedWhere_{Guid.NewGuid():N}");
        return factory.Create(path, "regression-test-password");
    }
}
