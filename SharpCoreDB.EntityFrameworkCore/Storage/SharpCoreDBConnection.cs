using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace SharpCoreDB.EntityFrameworkCore.Storage;

/// <summary>
/// Represents a SharpCoreDB database connection for Entity Framework Core.
/// Modern C# 14 implementation with improved resource management.
/// </summary>
public class SharpCoreDBConnection : DbConnection
{
    private readonly IServiceProvider _services;
    private IDatabase? _database;
    private readonly ConnectionStringBuilder _connectionStringBuilder;
    private ConnectionState _state = ConnectionState.Closed;
    private DatabasePool? _pool;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBConnection"/> class.
    /// </summary>
    /// <param name="services">The service provider.</param>
    /// <param name="connectionString">The connection string.</param>
    public SharpCoreDBConnection(IServiceProvider services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services); // ? C# 14
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString); // ? C# 14
        
        _services = services;
        _connectionStringBuilder = new ConnectionStringBuilder(connectionString);
        ConnectionString = connectionString;
    }

    /// <inheritdoc />
    [AllowNull]
    public override string ConnectionString { get; set; }

    /// <inheritdoc />
    public override string Database => _connectionStringBuilder.DataSource;

    /// <inheritdoc />
    public override string DataSource => _connectionStringBuilder.DataSource;

    /// <inheritdoc />
    public override string ServerVersion => "1.0.0";

    /// <inheritdoc />
    public override ConnectionState State => _state;

    /// <summary>
    /// Gets the underlying SharpCoreDB database instance.
    /// </summary>
    public IDatabase? DbInstance => _database;

    /// <inheritdoc />
    public override void ChangeDatabase(string databaseName)
    {
        throw new NotSupportedException("SharpCoreDB does not support changing databases.");
    }

    /// <inheritdoc />
    public override void Close()
    {
        if (_state == ConnectionState.Closed)
            return;

        if (_database is not null && _pool is not null) // ? C# 14: not pattern
        {
            _pool.ReturnDatabase(_database);
        }

        _state = ConnectionState.Closed;
    }

    /// <inheritdoc />
    public override void Open()
    {
        if (_state == ConnectionState.Open)
            return;

        ArgumentException.ThrowIfNullOrWhiteSpace(_connectionStringBuilder.DataSource, nameof(DataSource)); // ? C# 14

        // Check if pooling is enabled
        var usePooling = _connectionStringBuilder.Cache?.Equals("Shared", StringComparison.OrdinalIgnoreCase) == true;

        if (usePooling)
        {
            _pool = _services.GetService<DatabasePool>();
            _pool ??= new DatabasePool(_services, maxPoolSize: 10); // ? C# 14: ??= operator

            _database = _pool.GetDatabase(ConnectionString);
        }
        else
        {
            var factory = _services.GetRequiredService<DatabaseFactory>();
            _database = factory.Create(
                _connectionStringBuilder.DataSource,
                _connectionStringBuilder.Password ?? "default",
                _connectionStringBuilder.ReadOnly);
        }

        _state = ConnectionState.Open;
    }

    /// <inheritdoc />
    public override async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        if (_state == ConnectionState.Open)
            return;

        ArgumentException.ThrowIfNullOrWhiteSpace(_connectionStringBuilder.DataSource, nameof(DataSource)); // ? C# 14

        // Check if pooling is enabled
        var usePooling = _connectionStringBuilder.Cache?.Equals("Shared", StringComparison.OrdinalIgnoreCase) == true;

        if (usePooling)
        {
            _pool = _services.GetService<DatabasePool>();
            _pool ??= new DatabasePool(_services, maxPoolSize: 10);

            _database = await Task.Run(() => _pool.GetDatabase(ConnectionString), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var factory = _services.GetRequiredService<DatabaseFactory>();
            _database = await Task.Run(() => factory.Create(
                _connectionStringBuilder.DataSource,
                _connectionStringBuilder.Password ?? "default",
                _connectionStringBuilder.ReadOnly), cancellationToken).ConfigureAwait(false);
        }

        _state = ConnectionState.Open;
    }

    /// <inheritdoc />
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        if (_state != ConnectionState.Open)
            throw new InvalidOperationException("Connection must be open to begin a transaction.");

        return new SharpCoreDBDbTransaction(this, isolationLevel);
    }

    /// <inheritdoc />
    protected override DbCommand CreateDbCommand() => new SharpCoreDBCommand(this); // ? C# 14: expression-bodied

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
        }

        base.Dispose(disposing);
    }
}
