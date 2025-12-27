using SharpCoreDB;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SharpCoreDB.Tests;

/// <summary>
/// Metadata discovery tests for tables and columns.
/// </summary>
public sealed class MetadataTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseFactory _factory;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataTests"/> class.
    /// </summary>
    public MetadataTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"meta_{Guid.NewGuid()}");
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
    }

    /// <summary>
    /// Verifies tables and columns are exposed via metadata provider.
    /// </summary>
    [Fact]
    public void MetadataProvider_ReturnsTablesAndColumns()
    {
        var db = _factory.Create(_dbPath, "pwd");
        db.ExecuteSQL("CREATE TABLE meta_test (id INTEGER, name TEXT)");

        var meta = db as IMetadataProvider;
        Assert.NotNull(meta);

        var tables = meta!.GetTables();
        Assert.Contains(tables, t => t.Name == "meta_test");

        var columns = meta.GetColumns("meta_test");
        Assert.Equal(2, columns.Count);
        Assert.Contains(columns, c => c.Name == "id" && c.DataType == DataType.Integer.ToString());
        Assert.Contains(columns, c => c.Name == "name" && c.DataType == DataType.String.ToString());
    }

    /// <summary>
    /// Disposes test resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            try
            {
                if (Directory.Exists(_dbPath))
                {
                    Directory.Delete(_dbPath, true);
                }
            }
            catch
            {
                // ignore cleanup errors
            }
        }

        _disposed = true;
    }
}
