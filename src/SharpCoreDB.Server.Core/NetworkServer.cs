// <copyright file="NetworkServer.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpCoreDB.Server.Core;
using SharpCoreDB.Server.Core.Catalog;
using SharpCoreDB.Server.Core.Observability;
using SharpCoreDB.Server.Core.Security;
using SharpCoreDB.Server.Core.Tenancy;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace SharpCoreDB.Server.Core;

/// <summary>
/// Main network server for SharpCoreDB.
/// Manages connections, authentication, TLS policy, and hosted databases.
/// C# 14: Uses primary constructor for dependency injection.
/// </summary>
public sealed class NetworkServer(
    IOptions<ServerConfiguration> configuration,
    ILogger<NetworkServer> logger,
    ILoggerFactory loggerFactory,
    DatabaseRegistry databaseRegistry,
    SessionManager sessionManager,
    UserAuthenticationService authService,
    TenantAuthorizationPolicyService tenantAuthorizationPolicyService,
    PgCatalogService pgCatalogService,
    MetricsCollector metricsCollector) : IAsyncDisposable
{
    private readonly ServerConfiguration _config = configuration.Value;
    private readonly ILogger<NetworkServer> _logger = logger;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private readonly DatabaseRegistry _databaseRegistry = databaseRegistry;
    private readonly SessionManager _sessionManager = sessionManager;
    private readonly UserAuthenticationService _authService = authService;
    private readonly TenantAuthorizationPolicyService _tenantAuthorizationPolicyService = tenantAuthorizationPolicyService;
    private readonly PgCatalogService _pgCatalogService = pgCatalogService;
    private readonly MetricsCollector _metricsCollector = metricsCollector;
    private readonly ConcurrentDictionary<string, ClientConnection> _connections = new();
    private readonly Lock _lifecycleLock = new();
    private bool _isRunning;
    private CancellationTokenSource? _shutdownCts;
    private long _startTimestamp;
    private long _totalConnectionsServed;

    // Protocol handlers
    private TcpListener? _binaryProtocolListener;
    private BinaryProtocolHandler? _binaryProtocolHandler;

    /// <summary>
    /// Gets the server status.
    /// </summary>
    public ServerStatus Status => _isRunning ? ServerStatus.Running : ServerStatus.Stopped;

    /// <summary>
    /// Gets active connection count.
    /// </summary>
    public int ActiveConnections => _connections.Count;

    /// <summary>
    /// Starts the network server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();

        lock (_lifecycleLock)
        {
            if (_isRunning)
            {
                throw new InvalidOperationException("Server is already running");
            }

            _isRunning = true;
            _shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _startTimestamp = Stopwatch.GetTimestamp();
        }

        _logger.LogInformation("Starting SharpCoreDB Server v1.5.0");
        _logger.LogInformation("Server name: {ServerName}", _config.ServerName);
        _logger.LogInformation("Bind address: {BindAddress}", _config.BindAddress);
        _logger.LogInformation("gRPC HTTPS port: {GrpcPort}", _config.GrpcPort);
        _logger.LogInformation("Binary protocol enabled: {EnableBinaryProtocol}", _config.EnableBinaryProtocol);
        _logger.LogInformation("Binary protocol port: {BinaryProtocolPort}", _config.BinaryProtocolPort);
        _logger.LogInformation("HTTPS API enabled: {EnableHttpsApi}", _config.EnableHttpsApi);
        _logger.LogInformation("Hosted databases: {DatabaseCount}", _config.Databases.Count);
        _logger.LogInformation("Default database: {DefaultDatabase}", _config.DefaultDatabase);
        _logger.LogInformation("System databases enabled: {SystemDatabasesEnabled}", _config.SystemDatabases.Enabled);
        _logger.LogInformation("TLS minimum version: {TlsMinimumVersion}", _config.Security.MinimumTlsVersion);
        _logger.LogInformation("Max connections: {MaxConnections}", _config.MaxConnections);

        // Initialize components (placeholder for Phase 1)
        await InitializeServerAsync(_shutdownCts.Token);

        _logger.LogInformation("SharpCoreDB Server started successfully");
    }

    /// <summary>
    /// Stops the network server gracefully.
    /// </summary>
    public async Task StopAsync()
    {
        lock (_lifecycleLock)
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
        }

        _logger.LogInformation("Stopping SharpCoreDB Server...");

        // Signal shutdown
        _shutdownCts?.Cancel();

        // Stop protocol listeners
        _binaryProtocolListener?.Stop();

        // Close all connections gracefully
        var closeTasks = _connections.Values.Select(conn => CloseConnectionAsync(conn));
        await Task.WhenAll(closeTasks);

        // Shutdown database registry
        await _databaseRegistry.ShutdownAsync();

        _logger.LogInformation("SharpCoreDB Server stopped");
    }

    /// <summary>
    /// Registers a new client connection.
    /// </summary>
    /// <param name="connectionId">Unique connection ID.</param>
    /// <param name="connection">Client connection instance.</param>
    public bool RegisterConnection(string connectionId, ClientConnection connection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(connection);

        if (_connections.Count >= _config.MaxConnections)
        {
            _logger.LogWarning("Max connections ({MaxConnections}) reached. Rejecting connection {ConnectionId}",
                _config.MaxConnections, connectionId);
            return false;
        }

        if (_connections.TryAdd(connectionId, connection))
        {
            Interlocked.Increment(ref _totalConnectionsServed);
            _logger.LogDebug("Connection registered: {ConnectionId}", connectionId);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Unregisters a client connection.
    /// </summary>
    public bool UnregisterConnection(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out _))
        {
            _logger.LogDebug("Connection unregistered: {ConnectionId}", connectionId);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets server health metrics.
    /// </summary>
    public ServerHealthMetrics GetHealthMetrics()
    {
        return new ServerHealthMetrics
        {
            Status = Status,
            ActiveConnections = _connections.Count,
            TotalConnectionsServed = Interlocked.Read(ref _totalConnectionsServed),
            UptimeSeconds = _isRunning ? (long)Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds : 0,
            MemoryUsageBytes = GC.GetTotalMemory(forceFullCollection: false),
        };
    }

    private void ValidateConfiguration()
    {
        if (!_config.EnableGrpc)
        {
            throw new InvalidOperationException("gRPC must be enabled for SharpCoreDB server.");
        }

        if (_config.EnableBinaryProtocol && (_config.BinaryProtocolPort is <= 0 or > 65535))
        {
            throw new InvalidOperationException($"BinaryProtocolPort '{_config.BinaryProtocolPort}' is invalid.");
        }

        if (!_config.Security.TlsEnabled)
        {
            throw new InvalidOperationException("TLS must be enabled. Plain HTTP endpoints are not allowed.");
        }

        if (string.IsNullOrWhiteSpace(_config.Security.TlsCertificatePath))
        {
            throw new InvalidOperationException("TLS certificate path is required when TLS is enabled.");
        }

        if (_config.Databases.Count == 0)
        {
            throw new InvalidOperationException("At least one hosted database must be configured.");
        }

        if (string.IsNullOrWhiteSpace(_config.DefaultDatabase))
        {
            throw new InvalidOperationException("DefaultDatabase is required.");
        }

        if (!_config.Databases.Any(d => d.Name.Equals(_config.DefaultDatabase, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Default database '{_config.DefaultDatabase}' is not present in Databases.");
        }

        var duplicateName = _config.Databases
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicateName is not null)
        {
            throw new InvalidOperationException($"Duplicate database name configured: '{duplicateName.Key}'.");
        }

        if (_config.SystemDatabases.Enabled)
        {
            EnsureSystemDatabaseExists(_config.SystemDatabases.MasterDatabaseName);
            EnsureSystemDatabaseExists(_config.SystemDatabases.ModelDatabaseName);
            EnsureSystemDatabaseExists(_config.SystemDatabases.MsdbDatabaseName);
            EnsureSystemDatabaseExists(_config.SystemDatabases.TempDbDatabaseName);
        }
    }

    private void EnsureSystemDatabaseExists(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("System database names cannot be empty.");
        }

        if (!_config.Databases.Any(d => d.Name.Equals(databaseName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"System database '{databaseName}' is enabled but not present in Databases collection.");
        }
    }

    private async Task InitializeServerAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing server components...");

        // Initialize database registry
        await _databaseRegistry.InitializeAsync(cancellationToken);

        // Bootstrap tenant catalog schema in master database
        var masterDatabase = _databaseRegistry.GetDatabase(_config.SystemDatabases.MasterDatabaseName)
            ?? _databaseRegistry.GetDatabase(_config.DefaultDatabase)
            ?? throw new InvalidOperationException("Unable to resolve master database for tenant catalog bootstrap.");

        var catalogRepository = new TenantCatalogRepository(
            masterDatabase,
            _loggerFactory.CreateLogger<TenantCatalogRepository>());

        await catalogRepository.InitializeCatalogAsync(cancellationToken);

        var grantsRepository = new DatabaseGrantsRepository(
            masterDatabase,
            new TenantSecurityAuditService(
                new TenantSecurityAuditStore(),
                _loggerFactory.CreateLogger<TenantSecurityAuditService>()),
            _loggerFactory.CreateLogger<DatabaseGrantsRepository>());

        await grantsRepository.InitializeGrantsSchemaAsync(cancellationToken);

        // Initialize binary protocol handler
        _binaryProtocolHandler = new BinaryProtocolHandler(
            _databaseRegistry,
            _sessionManager,
            _authService,
            _tenantAuthorizationPolicyService,
            _pgCatalogService,
            _metricsCollector,
            _loggerFactory.CreateLogger<BinaryProtocolHandler>());

        if (_config.EnableBinaryProtocol)
        {
            // Start binary protocol listener (PostgreSQL compatible)
            _binaryProtocolListener = new TcpListener(IPAddress.Parse(_config.BindAddress), _config.BinaryProtocolPort);
            _binaryProtocolListener.Start();

            _logger.LogInformation("Binary protocol listener started on {Address}:{Port}",
                _config.BindAddress, _config.BinaryProtocolPort);

            // Start accepting binary protocol connections
            _ = Task.Run(() => AcceptBinaryConnectionsAsync(_shutdownCts!.Token), _shutdownCts!.Token);
        }
        else
        {
            _logger.LogInformation("Binary protocol listener disabled by configuration.");
        }

        _logger.LogInformation("Server components initialized successfully");
    }

    /// <summary>
    /// Accepts incoming binary protocol connections.
    /// </summary>
    private async Task AcceptBinaryConnectionsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _binaryProtocolListener!.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleBinaryConnectionAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting binary protocol connection");
            }
        }
    }

    /// <summary>
    /// Handles a binary protocol connection.
    /// </summary>
    private async Task HandleBinaryConnectionAsync(TcpClient client, CancellationToken cancellationToken)
    {
        if (_binaryProtocolHandler == null)
        {
            client.Close();
            return;
        }

        try
        {
            await _binaryProtocolHandler.HandleConnectionAsync(client, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling binary protocol connection");
        }
        finally
        {
            client.Close();
        }
    }

    private async Task CloseConnectionAsync(ClientConnection connection)
    {
        try
        {
            await connection.CloseAsync();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Error closing connection {ConnectionId}", connection.ConnectionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _shutdownCts?.Dispose();
    }
}

/// <summary>
/// Server status enumeration.
/// </summary>
public enum ServerStatus
{
    Stopped,
    Starting,
    Running,
    Stopping,
}

/// <summary>
/// Server health metrics.
/// </summary>
public sealed class ServerHealthMetrics
{
    public required ServerStatus Status { get; init; }
    public required int ActiveConnections { get; init; }
    public required long TotalConnectionsServed { get; init; }
    public required long UptimeSeconds { get; init; }
    public required long MemoryUsageBytes { get; init; }
}

/// <summary>
/// Represents a client connection (placeholder for Phase 1).
/// </summary>
public sealed class ClientConnection(string connectionId)
{
    public string ConnectionId { get; } = connectionId;

    public async Task CloseAsync()
    {
        // Placeholder: will implement connection cleanup
        await Task.CompletedTask;
    }
}
