using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Tests;

/// <summary>
/// Fixture for creating temporary databases for integration tests.
/// Provides a clean database instance for each test.
/// </summary>
public class TempDatabaseFixture : IDisposable
{
    private readonly string _tempDbPath;
    private readonly IServiceProvider _serviceProvider;
    private readonly DatabaseFactory _factory;
    private IDatabase? _database;

    public TempDatabaseFixture()
    {
        // Create a unique temporary database path
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"SharpCoreDB_Test_{Guid.NewGuid()}");

        // Set up dependency injection
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _serviceProvider = services.BuildServiceProvider();
        _factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
    }

    /// <summary>
    /// Gets or creates a database instance.
    /// </summary>
    public IDatabase Database => _database ??= _factory.Create(_tempDbPath, "testPassword");

    /// <summary>
    /// Creates a new database with custom configuration.
    /// </summary>
    public IDatabase CreateDatabase(DatabaseConfig? config = null, SecurityConfig? securityConfig = null)
    {
        return _factory.Create(_tempDbPath, "testPassword", config: config, securityConfig: securityConfig);
    }

    /// <summary>
    /// Creates a readonly database instance.
    /// </summary>
    public IDatabase CreateReadOnlyDatabase()
    {
        return _factory.Create(_tempDbPath, "testPassword", isReadOnly: true);
    }

    /// <summary>
    /// Gets the temporary database path.
    /// </summary>
    public string DatabasePath => _tempDbPath;

    public void Dispose()
    {
        // Clean up the temporary database
        if (Directory.Exists(_tempDbPath))
        {
            Directory.Delete(_tempDbPath, true);
        }
    }
}