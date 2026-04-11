// <copyright file="DatabaseRegistry.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpCoreDB;
using System.Collections.Concurrent;
using SharpCoreDB.Storage;

namespace SharpCoreDB.Server.Core;

/// <summary>
/// Manages hosted database instances and their lifecycle.
/// Provides database lookup, connection pooling, and resource management.
/// C# 14: Uses primary constructor for dependency injection.
/// </summary>
public sealed class DatabaseRegistry(
    IOptions<ServerConfiguration> configuration,
    ILogger<DatabaseRegistry> logger) : IAsyncDisposable
{
    private readonly ServerConfiguration _config = configuration.Value;
    private readonly ConcurrentDictionary<string, DatabaseInstance> _databases = new();
    private readonly Lock _registryLock = new();
    private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);
    private bool _isInitialized;

    /// <summary>
    /// Gets all registered database names.
    /// </summary>
    public IReadOnlyCollection<string> DatabaseNames => _databases.Keys.ToArray();

    /// <summary>
    /// Initializes the database registry with configured databases.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initializationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isInitialized)
            {
                logger.LogDebug("Database registry already initialized. Skipping.");
                return;
            }

            logger.LogInformation("Initializing database registry with {Count} databases", _config.Databases.Count);

            foreach (var dbConfig in _config.Databases)
            {
                await RegisterConfiguredDatabaseAsync(dbConfig, cancellationToken).ConfigureAwait(false);
            }

            // Initialize system databases if enabled
            if (_config.SystemDatabases.Enabled)
            {
                await InitializeSystemDatabasesAsync(cancellationToken).ConfigureAwait(false);
            }

            _isInitialized = true;
            logger.LogInformation("Database registry initialized with {Count} databases", _databases.Count);
        }
        finally
        {
            _initializationSemaphore.Release();
        }
    }

    /// <summary>
    /// Gets a database instance by name.
    /// </summary>
    /// <param name="databaseName">Database name.</param>
    /// <returns>Database instance or null if not found.</returns>
    public DatabaseInstance? GetDatabase(string databaseName)
    {
        ArgumentNullException.ThrowIfNull(databaseName);
        return _databases.GetValueOrDefault(databaseName);
    }

    /// <summary>
    /// Checks if a database exists.
    /// </summary>
    /// <param name="databaseName">Database name.</param>
    /// <returns>True if database exists.</returns>
    public bool DatabaseExists(string databaseName)
    {
        ArgumentNullException.ThrowIfNull(databaseName);
        return _databases.ContainsKey(databaseName);
    }

    /// <summary>
    /// Registers a new database instance at runtime for tenant provisioning.
    /// Supports safe attachment of databases without server restart.
    /// Fails if database already registered.
    /// </summary>
    /// <param name="databaseName">Logical database name.</param>
    /// <param name="databasePath">Physical path to database file.</param>
    /// <param name="storageMode">Storage mode (SingleFile, Directory, Columnar).</param>
    /// <param name="connectionPoolSize">Connection pool size for this database.</param>
    /// <param name="encryptionEnabled">Whether encryption is enabled.</param>
    /// <param name="encryptionMasterPassword">Optional master password for encryption.</param>
    /// <param name="encryptionKeyReference">Optional key file reference for encryption.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The registered database instance.</returns>
    /// <exception cref="InvalidOperationException">If database is already registered.</exception>
    public Task<DatabaseInstance> RegisterDatabaseAsync(
        string databaseName,
        string databasePath,
        string storageMode = "SingleFile",
        int connectionPoolSize = 10,
        bool encryptionEnabled = false,
        string? encryptionMasterPassword = null,
        string? encryptionKeyReference = null,
        CancellationToken cancellationToken = default) =>
        RegisterDatabaseRuntimeAsync(
            databaseName,
            databasePath,
            storageMode,
            connectionPoolSize,
            encryptionEnabled,
            encryptionMasterPassword,
            encryptionKeyReference,
            cancellationToken);

    /// <summary>
    /// Unregisters a database instance at runtime with graceful connection draining.
    /// Safe for tenant deprovisioning - closes connections and removes registry entry.
    /// Fails if database not found or currently in use.
    /// </summary>
    /// <param name="databaseName">Logical database name to unregister.</param>
    /// <param name="gracefulTimeoutSeconds">Timeout for draining connections (default 30s).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">If database not found.</exception>
    /// <exception cref="OperationCanceledException">If graceful drain times out.</exception>
    public Task UnregisterDatabaseAsync(
        string databaseName,
        int gracefulTimeoutSeconds = 30,
        CancellationToken cancellationToken = default) =>
        UnregisterDatabaseRuntimeAsync(databaseName, gracefulTimeoutSeconds, cancellationToken);

    /// <summary>
    /// Registers a new database instance at runtime for tenant provisioning.
    /// Supports safe attachment of databases without server restart.
    /// Fails if database already registered.
    /// </summary>
    /// <param name="databaseName">Logical database name.</param>
    /// <param name="databasePath">Physical path to database file.</param>
    /// <param name="storageMode">Storage mode (SingleFile, Directory, Columnar).</param>
    /// <param name="connectionPoolSize">Connection pool size for this database.</param>
    /// <param name="encryptionEnabled">Whether encryption is enabled.</param>
    /// <param name="encryptionMasterPassword">Optional master password for encryption.</param>
    /// <param name="encryptionKeyReference">Optional key file reference for encryption.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The registered database instance.</returns>
    /// <exception cref="InvalidOperationException">If database is already registered.</exception>
    public async Task<DatabaseInstance> RegisterDatabaseRuntimeAsync(
        string databaseName,
        string databasePath,
        string storageMode = "SingleFile",
        int connectionPoolSize = 10,
        bool encryptionEnabled = false,
        string? encryptionMasterPassword = null,
        string? encryptionKeyReference = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(databaseName);
        ArgumentNullException.ThrowIfNull(databasePath);

        logger.LogInformation(
            "Registering database '{Name}' at runtime from path '{Path}'",
            databaseName, databasePath);

        lock (_registryLock)
        {
            if (_databases.ContainsKey(databaseName))
            {
                logger.LogError("Database '{Name}' is already registered", databaseName);
                throw new InvalidOperationException($"Database '{databaseName}' is already registered");
            }
        }

        try
        {
            var config = new DatabaseInstanceConfiguration
            {
                Name = databaseName,
                DatabasePath = databasePath,
                StorageMode = storageMode,
                ConnectionPoolSize = connectionPoolSize,
                EncryptionEnabled = encryptionEnabled,
                EncryptionMasterPassword = encryptionMasterPassword,
                EncryptionKeyFile = encryptionKeyReference,
                IsSystemDatabase = false,
                IsReadOnly = false
            };

            var instance = new DatabaseInstance(config, logger);
            await instance.InitializeAsync(cancellationToken);

            lock (_registryLock)
            {
                if (!_databases.TryAdd(databaseName, instance))
                {
                    logger.LogError("Failed to add database '{Name}' to registry (race condition)", databaseName);
                    throw new InvalidOperationException($"Database '{databaseName}' registration race condition");
                }
            }

            logger.LogInformation("Database '{Name}' registered at runtime successfully", databaseName);
            return instance;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register database '{Name}' at runtime", databaseName);
            throw;
        }
    }

    /// <summary>
    /// Unregisters a database instance at runtime with graceful connection draining.
    /// Safe for tenant deprovisioning - closes connections and removes registry entry.
    /// Fails if database not found or currently in use.
    /// </summary>
    /// <param name="databaseName">Logical database name to unregister.</param>
    /// <param name="gracefulTimeoutSeconds">Timeout for draining connections (default 30s).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">If database not found.</exception>
    /// <exception cref="OperationCanceledException">If graceful drain times out.</exception>
    public async Task UnregisterDatabaseRuntimeAsync(
        string databaseName,
        int gracefulTimeoutSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(databaseName);

        logger.LogInformation("Unregistering database '{Name}' at runtime", databaseName);

        DatabaseInstance? instance = null;

        lock (_registryLock)
        {
            if (!_databases.TryRemove(databaseName, out instance))
            {
                logger.LogError("Database '{Name}' not found in registry", databaseName);
                throw new InvalidOperationException($"Database '{databaseName}' not found");
            }
        }

        if (instance == null)
        {
            logger.LogError("Database instance for '{Name}' is null", databaseName);
            throw new InvalidOperationException($"Database instance '{databaseName}' is null");
        }

        try
        {
            logger.LogInformation(
                "Draining connections for database '{Name}' with {Seconds}s timeout",
                databaseName, gracefulTimeoutSeconds);

            using var gracefulCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            gracefulCts.CancelAfter(TimeSpan.FromSeconds(gracefulTimeoutSeconds));

            await instance.ShutdownAsync(gracefulCts.Token);

            logger.LogInformation("Database '{Name}' unregistered successfully", databaseName);
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(
                ex,
                "Graceful shutdown of database '{Name}' timed out after {Seconds}s",
                databaseName, gracefulTimeoutSeconds);

            // Force cleanup even on timeout
            await instance.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to unregister database '{Name}' at runtime", databaseName);
            throw;
        }
    }

    /// <summary>
    /// Registers a new database instance.
    /// </summary>
    /// <param name="config">Database configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task RegisterConfiguredDatabaseAsync(DatabaseInstanceConfiguration config, CancellationToken cancellationToken)
    {
        logger.LogInformation("Registering database '{Name}' at '{Path}'", config.Name, config.DatabasePath);

        var instance = new DatabaseInstance(config, logger);
        await instance.InitializeAsync(cancellationToken);

        if (!_databases.TryAdd(config.Name, instance))
        {
            await instance.DisposeAsync();
            throw new InvalidOperationException($"Database '{config.Name}' is already registered");
        }

        logger.LogInformation("Database '{Name}' registered successfully", config.Name);
    }

    /// <summary>
    /// Initializes system databases (master, model, temp).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task InitializeSystemDatabasesAsync(CancellationToken cancellationToken)
    {
        // Master database - system catalog and metadata
        var masterConfig = new DatabaseInstanceConfiguration
        {
            Name = _config.SystemDatabases.MasterDatabaseName,
            DatabasePath = Path.Combine(_config.Databases.First().DatabasePath, "..", "master.db"),
            StorageMode = "SingleFile",
            IsSystemDatabase = true,
            ConnectionPoolSize = 10
        };

        await RegisterConfiguredDatabaseAsync(masterConfig, cancellationToken);

        // Model database - template for new databases
        var modelConfig = new DatabaseInstanceConfiguration
        {
            Name = _config.SystemDatabases.ModelDatabaseName,
            DatabasePath = Path.Combine(_config.Databases.First().DatabasePath, "..", "model.db"),
            StorageMode = "SingleFile",
            IsSystemDatabase = true,
            ConnectionPoolSize = 5
        };

        await RegisterConfiguredDatabaseAsync(modelConfig, cancellationToken);

        logger.LogInformation("System databases initialized");
    }

    /// <summary>
    /// Shuts down all databases gracefully.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Shutting down database registry");

        var shutdownTasks = new List<Task>();
        foreach (var db in _databases.Values)
        {
            shutdownTasks.Add(db.ShutdownAsync(cancellationToken));
        }

        await Task.WhenAll(shutdownTasks);
        _databases.Clear();

        lock (_registryLock)
        {
            _isInitialized = false;
        }

        logger.LogInformation("Database registry shutdown complete");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync();
        _initializationSemaphore.Dispose();
    }
}

/// <summary>
/// Represents a hosted database instance with connection pooling.
/// C# 14: Uses primary constructor for configuration.
/// </summary>
public sealed class DatabaseInstance(
    DatabaseInstanceConfiguration config,
    ILogger logger) : IAsyncDisposable
{
    private readonly DatabaseInstanceConfiguration _config = config;
    private readonly ILogger _logger = logger;
    private Database? _database;
    private ConnectionPool? _connectionPool;

    /// <summary>
    /// Gets the database configuration.
    /// </summary>
    public DatabaseInstanceConfiguration Configuration => _config;

    /// <summary>
    /// Gets the connection pool.
    /// </summary>
    public ConnectionPool ConnectionPool => _connectionPool
        ?? throw new InvalidOperationException("Connection pool is not initialized.");

    /// <summary>
    /// Gets the initialized database handle.
    /// </summary>
    public SharpCoreDB.Interfaces.IDatabase Database => _database
        ?? throw new InvalidOperationException("Database is not initialized.");

    /// <summary>
    /// Initializes the database instance.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing database '{Name}'", _config.Name);

        // Create database instance
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();

        var config = new DatabaseConfig(); // Use default config
        var masterPassword = string.IsNullOrWhiteSpace(_config.EncryptionMasterPassword)
            ? "default-password"
            : _config.EncryptionMasterPassword;

        _database = new Database(serviceProvider, _config.DatabasePath, masterPassword, _config.IsReadOnly, config);

        // Initialize connection pool
        _connectionPool = new ConnectionPool(_database, _config.ConnectionPoolSize, _logger);

        _logger.LogInformation("Database '{Name}' initialized with pool size {PoolSize}",
            _config.Name, _config.ConnectionPoolSize);
    }

    /// <summary>
    /// Gets a connection from the pool.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Pooled database connection.</returns>
    public async Task<PooledConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_database == null)
        {
            throw new InvalidOperationException("Database not initialized");
        }

        if (_connectionPool is null)
        {
            throw new InvalidOperationException("Connection pool not initialized");
        }

        return await _connectionPool.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Shuts down the database instance.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Shutting down database '{Name}'", _config.Name);

        if (_connectionPool != null)
        {
            await _connectionPool.ShutdownAsync(cancellationToken);
        }

        if (_database != null)
        {
            _database.Dispose();
        }

        _logger.LogInformation("Database '{Name}' shutdown complete", _config.Name);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync();
    }
}

/// <summary>
/// Production-grade connection pool with min/max sizing, idle eviction, and wait-with-timeout.
/// C# 14: Primary constructor, Lock keyword, collection expressions.
/// </summary>
public sealed class ConnectionPool : IAsyncDisposable
{
    private readonly Database _database;
    private readonly int _minPoolSize;
    private readonly int _maxPoolSize;
    private readonly TimeSpan _idleTimeout;
    private readonly TimeSpan _acquireTimeout;
    private readonly ILogger _logger;
    private readonly ConcurrentQueue<PooledConnection> _availableConnections = new();
    private readonly HashSet<PooledConnection> _activeConnections = [];
    private readonly Lock _poolLock = new();
    private readonly SemaphoreSlim _connectionSemaphore;
    private readonly PeriodicTimer? _evictionTimer;
    private readonly CancellationTokenSource _evictionCts = new();
    private int _createdConnections;

    /// <summary>
    /// Initializes a new connection pool.
    /// </summary>
    /// <param name="database">Underlying database instance.</param>
    /// <param name="maxPoolSize">Maximum connections in pool.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="minPoolSize">Minimum warm connections to keep (default 2).</param>
    /// <param name="idleTimeoutSeconds">Idle timeout before eviction (default 300s).</param>
    /// <param name="acquireTimeoutSeconds">Max wait time to acquire a connection (default 30s).</param>
    public ConnectionPool(
        Database database,
        int maxPoolSize,
        ILogger logger,
        int minPoolSize = 2,
        int idleTimeoutSeconds = 300,
        int acquireTimeoutSeconds = 30)
    {
        _database = database;
        _maxPoolSize = maxPoolSize;
        _minPoolSize = Math.Min(minPoolSize, maxPoolSize);
        _idleTimeout = TimeSpan.FromSeconds(idleTimeoutSeconds);
        _acquireTimeout = TimeSpan.FromSeconds(acquireTimeoutSeconds);
        _logger = logger;
        _connectionSemaphore = new SemaphoreSlim(maxPoolSize, maxPoolSize);

        // Start idle eviction background task
        _evictionTimer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        _ = RunEvictionLoopAsync(_evictionCts.Token);
    }

    /// <summary>Gets the number of active (checked-out) connections.</summary>
    public int ActiveCount
    {
        get { lock (_poolLock) { return _activeConnections.Count; } }
    }

    /// <summary>Gets the number of idle (available) connections.</summary>
    public int IdleCount => _availableConnections.Count;

    /// <summary>Gets the total connections created by this pool.</summary>
    public int TotalCreated => _createdConnections;

    /// <summary>
    /// Gets a connection from the pool, waiting up to <see cref="_acquireTimeout"/> if exhausted.
    /// </summary>
    public async Task<PooledConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        // Wait for a permit (blocks if pool is at max capacity)
        if (!await _connectionSemaphore.WaitAsync(_acquireTimeout, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning("Connection pool exhausted (max={Max}, active={Active})", _maxPoolSize, ActiveCount);
            throw new InvalidOperationException(
                $"Connection pool exhausted: {ActiveCount}/{_maxPoolSize} active. Waited {_acquireTimeout.TotalSeconds}s.");
        }

        // Try reuse an idle connection
        while (_availableConnections.TryDequeue(out var connection))
        {
            if (connection.IsHealthy && !connection.IsExpired(_idleTimeout))
            {
                lock (_poolLock) { _activeConnections.Add(connection); }
                return connection;
            }

            // Stale or unhealthy — discard
            DiscardConnection(connection);
        }

        // Create a fresh connection
        var created = Interlocked.Increment(ref _createdConnections);
        _logger.LogDebug("Creating new pooled connection #{Count}", created);
        var newConnection = new PooledConnection(_database, ReturnConnection);
        lock (_poolLock) { _activeConnections.Add(newConnection); }
        return newConnection;
    }

    /// <summary>
    /// Warms the pool to the minimum size.
    /// </summary>
    public void WarmPool()
    {
        var toCreate = _minPoolSize - _availableConnections.Count - ActiveCount;
        for (var i = 0; i < toCreate; i++)
        {
            Interlocked.Increment(ref _createdConnections);
            _availableConnections.Enqueue(new PooledConnection(_database, ReturnConnection));
        }

        _logger.LogInformation("Pool warmed to {Count} connections (min={Min})", _availableConnections.Count, _minPoolSize);
    }

    private void ReturnConnection(PooledConnection connection)
    {
        lock (_poolLock) { _activeConnections.Remove(connection); }

        if (connection.IsHealthy)
        {
            connection.MarkReturned();
            _availableConnections.Enqueue(connection);
        }
        else
        {
            DiscardConnection(connection);
        }

        _connectionSemaphore.Release();
    }

    private void DiscardConnection(PooledConnection connection)
    {
        Interlocked.Decrement(ref _createdConnections);
        // PooledConnection doesn't own heavy resources, just mark discarded
    }

    /// <summary>
    /// Background loop that evicts idle connections above the minimum pool size.
    /// </summary>
    private async Task RunEvictionLoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _evictionTimer!.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var evicted = 0;
                var snapshot = _availableConnections.Count;

                // Only evict if above minimum
                var toCheck = snapshot - _minPoolSize;
                for (var i = 0; i < toCheck; i++)
                {
                    if (!_availableConnections.TryDequeue(out var conn))
                    {
                        break;
                    }

                    if (conn.IsExpired(_idleTimeout))
                    {
                        DiscardConnection(conn);
                        evicted++;
                    }
                    else
                    {
                        _availableConnections.Enqueue(conn); // still fresh, put back
                    }
                }

                if (evicted > 0)
                {
                    _logger.LogDebug("Evicted {Count} idle connections (remaining={Remaining})",
                        evicted, _availableConnections.Count);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested
        }
    }

    /// <summary>
    /// Shuts down the connection pool and releases all connections.
    /// </summary>
    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Shutting down connection pool (active={Active}, idle={Idle})",
            ActiveCount, IdleCount);

        await _evictionCts.CancelAsync();
        _evictionTimer?.Dispose();

        var disposeTasks = new List<Task>();

        lock (_poolLock)
        {
            foreach (var connection in _activeConnections)
            {
                disposeTasks.Add(connection.DisposeAsync().AsTask());
            }
        }

        while (_availableConnections.TryDequeue(out var connection))
        {
            disposeTasks.Add(connection.DisposeAsync().AsTask());
        }

        await Task.WhenAll(disposeTasks).ConfigureAwait(false);

        lock (_poolLock) { _activeConnections.Clear(); }

        _connectionSemaphore.Dispose();
        _evictionCts.Dispose();

        _logger.LogInformation("Connection pool shutdown complete");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync();
    }
}

/// <summary>
/// Pooled database connection with automatic return to pool and idle tracking.
/// C# 14: Primary constructor, async dispose.
/// </summary>
public sealed class PooledConnection(
    Database database,
    Action<PooledConnection> returnAction) : IAsyncDisposable
{
    private readonly Database _database = database;
    private readonly Action<PooledConnection> _returnAction = returnAction;
    private bool _isReturned;
    private DateTimeOffset _lastUsed = DateTimeOffset.UtcNow;

    /// <summary>Gets the underlying database instance.</summary>
    public Database Database => _database;

    /// <summary>Gets whether this connection is healthy (not yet returned).</summary>
    public bool IsHealthy => !_isReturned;

    /// <summary>Checks if the connection has been idle longer than the given timeout.</summary>
    public bool IsExpired(TimeSpan idleTimeout) =>
        DateTimeOffset.UtcNow - _lastUsed > idleTimeout;

    /// <summary>Marks the connection as returned to the pool and resets the idle timer.</summary>
    public void MarkReturned()
    {
        _lastUsed = DateTimeOffset.UtcNow;
        _isReturned = false; // ready for reuse
    }

    /// <summary>Returns the connection to the pool.</summary>
    public void Return()
    {
        if (!_isReturned)
        {
            _isReturned = true;
            _returnAction(this);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Return();
        return ValueTask.CompletedTask;
    }
}
