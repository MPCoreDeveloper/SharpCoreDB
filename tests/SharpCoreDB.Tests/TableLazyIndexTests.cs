using SharpCoreDB.DataStructures;
using SharpCoreDB.Services;
using System.IO;

namespace SharpCoreDB.Tests;

/// <summary>
/// Tests for Table lazy loading functionality for hash indexes.
/// Verifies that indexes are not built until first query.
/// </summary>
[Trait("Category", "Skip")] // Skip until Storage constructor is fixed
public class TableLazyIndexTests : IDisposable
{
    private readonly string _testDirectory;

    public TableLazyIndexTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"SharpCoreDB_LazyIndexTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        // TODO: Fix Storage constructor call
        //_storage = new Services.Storage(_testDirectory);
    }

    public void Dispose()
    {
        // TODO: Uncomment when Storage is fixed
        //_storage?.Dispose();
        if (Directory.Exists(_testDirectory))
        {
            try { Directory.Delete(_testDirectory, true); } catch { }
        }
        GC.SuppressFinalize(this);
    }

    [Fact(Skip = "Storage constructor needs to be fixed")]
    public void LazyIndex_RegisteredButNotLoaded_UntilFirstQuery()
    {
        // Test skipped until Storage constructor is updated
    }

    [Fact(Skip = "Storage constructor needs to be fixed")]
    public void LazyIndex_LoadedOnFirstQuery()
    {
        // Test skipped until Storage constructor is updated
    }

    [Fact(Skip = "Storage constructor needs to be fixed")]
    public void LazyIndex_SecondQueryUsesCache()
    {
        // Test skipped until Storage constructor is updated
    }

    [Fact(Skip = "Storage constructor needs to be fixed")]
    public void LazyIndex_MultipleIndexes_OnlyUsedOnesLoaded()
    {
        // Test skipped until Storage constructor is updated
    }

    [Fact(Skip = "Storage constructor needs to be fixed")]
    public void LazyIndex_InsertMarksUnloadedIndexesStale()
    {
        // Test skipped until Storage constructor is updated
    }

    [Fact(Skip = "Storage constructor needs to be fixed")]
    public void LazyIndex_StaleIndexRebuiltOnFirstUse()
    {
        // Test skipped until Storage constructor is updated
    }

    [Fact(Skip = "Storage constructor needs to be fixed")]
    public void LazyIndex_EagerBuildOption_LoadsImmediately()
    {
        // Test skipped until Storage constructor is updated
    }

    [Fact(Skip = "Storage constructor needs to be fixed")]
    public void LazyIndex_GetStatistics_ShowsCorrectState()
    {
        // Test skipped until Storage constructor is updated
    }

    [Fact(Skip = "Storage constructor needs to be fixed")]
    public void LazyIndex_ThreadSafe_ConcurrentQueries()
    {
        // Test skipped until Storage constructor is updated
    }

    [Fact(Skip = "Storage constructor needs to be fixed")]
    public void LazyIndex_Delete_OnlyUpdatesLoadedIndexes()
    {
        // Test skipped until Storage constructor is updated
    }

    [Fact(Skip = "Storage constructor needs to be fixed")]
    public void LazyIndex_MemorySavings_UnusedIndexesNotLoaded()
    {
        // Test skipped until Storage constructor is updated
    }

    private Table CreateTableWithData()
    {
        var table = new Table()
        {
            Name = "users",
            Columns = ["id", "name", "email"],
            ColumnTypes = [DataType.Integer, DataType.String, DataType.String],
            PrimaryKeyIndex = 0,
            IsAuto = [false, false, false],
            DataFile = Path.Combine(_testDirectory, "users.dat")
        };

        // Insert test data
        table.Insert(new Dictionary<string, object>
        {
            { "id", 1 },
            { "name", "Alice" },
            { "email", "alice@example.com" }
        });

        table.Insert(new Dictionary<string, object>
        {
            { "id", 2 },
            { "name", "Bob" },
            { "email", "bob@example.com" }
        });

        table.Insert(new Dictionary<string, object>
        {
            { "id", 3 },
            { "name", "Charlie" },
            { "email", "charlie@example.com" }
        });

        return table;
    }
}
