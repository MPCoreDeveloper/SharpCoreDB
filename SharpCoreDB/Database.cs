using SharpCoreDB.Services;
using System.Text.Json;
using SharpCoreDB.Constants;
using SharpCoreDB.Interfaces;
using SharpCoreDB.DataStructures;
using Microsoft.Extensions.DependencyInjection;
namespace SharpCoreDB;

/// <summary>
/// Implementation of IDatabase.
/// </summary>
public class Database : IDatabase
{
    private readonly ICryptoService _crypto;
    private readonly IStorage _storage;
    private readonly IUserService _userService;
    private readonly Dictionary<string, ITable> _tables = [];
    private readonly string _dbPath;
    private readonly bool _isReadOnly;

    /// <summary>
    /// Initializes a new instance of the Database class.
    /// </summary>
    /// <param name="services">The service provider.</param>
    /// <param name="dbPath">The database path.</param>
    /// <param name="masterPassword">The master password.</param>
    /// <param name="isReadOnly">Whether the database is readonly.</param>
    public Database(IServiceProvider services, string dbPath, string masterPassword, bool isReadOnly = false)
    {
        _dbPath = dbPath;
        _isReadOnly = isReadOnly;
        Directory.CreateDirectory(_dbPath);
        _crypto = services.GetRequiredService<ICryptoService>();
        var masterKey = _crypto.DeriveKey(masterPassword, "salt");
        _storage = new Storage(_crypto, masterKey);
        _userService = new UserService(_crypto, _storage, _dbPath);
        Load();
    }

    private void Load()
    {
        var metaPath = Path.Combine(_dbPath, PersistenceConstants.MetaFileName);
        var metaJson = _storage.Read(metaPath);
        if (metaJson != null)
        {
            var meta = JsonSerializer.Deserialize<Dictionary<string, object>>(metaJson);
            if (meta != null && meta.TryGetValue(PersistenceConstants.TablesKey, out var tablesObj))
            {
                var tablesList = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(tablesObj.ToString());
                if (tablesList != null)
                {
                    foreach (var tableDict in tablesList)
                    {
                        var table = JsonSerializer.Deserialize<Table>(JsonSerializer.Serialize(tableDict));
                        table.SetStorage(_storage);
                        table.SetReadOnly(_isReadOnly);
                        if (table != null)
                        {
                            _tables[table.Name] = table;
                        }
                    }
                }
            }
        }
    }

    private void Save(WAL wal)
    {
        var tablesList = _tables.Values.Select(t => new
        {
            t.Name,
            t.Columns,
            t.ColumnTypes,
            t.PrimaryKeyIndex,
            t.DataFile
        }).ToList();
        var meta = new Dictionary<string, object> { [PersistenceConstants.TablesKey] = tablesList };
        _storage.Write(Path.Combine(_dbPath, PersistenceConstants.MetaFileName), JsonSerializer.Serialize(meta));
        wal.Commit();
    }

    /// <inheritdoc />
    public void ExecuteSQL(string sql)
    {
        var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts[0].ToUpper() == SqlConstants.SELECT)
        {
            var sqlParser = new SqlParser(_tables, null, _dbPath, _storage, _isReadOnly);
            sqlParser.Execute(sql, null);
        }
        else
        {
            using var wal = new WAL(_dbPath);
            var sqlParser = new SqlParser(_tables, wal, _dbPath, _storage, _isReadOnly);
            sqlParser.Execute(sql, wal);
            if (!_isReadOnly) Save(wal);
        }
    }

    /// <inheritdoc />
    public void CreateUser(string username, string password)
    {
        _userService.CreateUser(username, password);
    }

    /// <inheritdoc />
    public bool Login(string username, string password)
    {
        return _userService.Login(username, password);
    }

    /// <inheritdoc />
    public IDatabase Initialize(string dbPath, string masterPassword)
    {
        // Already initialized in constructor
        return this;
    }
}

/// <summary>
/// Extension methods for configuring SharpCoreDB services.
/// </summary>
public static class DatabaseExtensions
{
    /// <summary>
    /// Adds SharpCoreDB services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection with SharpCoreDB services added.</returns>
    public static IServiceCollection AddSharpCoreDB(this IServiceCollection services)
    {
        services.AddSingleton<ICryptoService, CryptoService>();
        services.AddTransient<DatabaseFactory>();
        return services;
    }
}

/// <summary>
/// Factory for creating Database instances.
/// </summary>
public class DatabaseFactory(IServiceProvider services)
{
    private readonly IServiceProvider _services = services;

    /// <summary>
    /// Creates a new Database instance and initializes it.
    /// </summary>
    /// <param name="dbPath">The database path.</param>
    /// <param name="masterPassword">The master password.</param>
    /// <param name="isReadOnly">Whether the database is readonly.</param>
    /// <returns>The initialized database.</returns>
    public IDatabase Create(string dbPath, string masterPassword, bool isReadOnly = false)
    {
        return new Database(_services, dbPath, masterPassword, isReadOnly);
    }
}
