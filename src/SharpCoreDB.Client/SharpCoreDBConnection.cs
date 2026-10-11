// <copyright file="SharpCoreDBConnection.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using System.Net;
using System.Net.Http;
using Grpc.Net.Client;
using SharpCoreDB.Server.Protocol;

namespace SharpCoreDB.Client;

/// <summary>
/// ADO.NET-like connection to SharpCoreDB network server.
/// C# 15: Uses primary constructor for immutability.
/// </summary>
public sealed class SharpCoreDBConnection : IAsyncDisposable
{
    private readonly string _connectionString;
    private GrpcChannel? _channel;
    private DatabaseService.DatabaseServiceClient? _client;
    private ConnectionState _state = ConnectionState.Closed;
    private readonly Lock _stateLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBConnection"/> class.
    /// </summary>
    /// <param name="connectionString">
    /// Connection string format: "Server=localhost;Port=5001;Database=mydb;SSL=true;Username=admin;Password=***"
    /// </param>
    public SharpCoreDBConnection(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
        ConnectionStringParser = new ConnectionStringParser(connectionString);
    }

    /// <summary>
    /// Gets the connection string parser.
    /// </summary>
    public ConnectionStringParser ConnectionStringParser { get; }

    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    public ConnectionState State => _state;

    /// <summary>
    /// Gets the server version (available after connection).
    /// </summary>
    public string? ServerVersion { get; private set; }

    /// <summary>
    /// Gets the session ID (available after connection).
    /// </summary>
    public string? SessionId { get; private set; }

    /// <summary>
    /// Opens the connection to the SharpCoreDB server.
    /// </summary>
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_state != ConnectionState.Closed)
            {
                throw new InvalidOperationException($"Connection is already {_state}");
            }

            _state = ConnectionState.Connecting;
        }

        try
        {
            var address = ConnectionStringParser.ServerAddress;

            var handler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(15),
                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
            };

            var channelOptions = new GrpcChannelOptions
            {
                HttpHandler = handler,
                MaxReceiveMessageSize = 100 * 1024 * 1024,
                MaxSendMessageSize = 100 * 1024 * 1024,
                HttpVersion = ConnectionStringParser.PreferHttp3 ? HttpVersion.Version30 : HttpVersion.Version20,
                HttpVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
            };

            _channel = GrpcChannel.ForAddress(address, channelOptions);
            _client = new DatabaseService.DatabaseServiceClient(_channel);

            var connectResponse = await _client.ConnectAsync(new ConnectRequest
            {
                DatabaseName = ConnectionStringParser.Database ?? "master",
                UserName = ConnectionStringParser.Username ?? "anonymous",
                Password = ConnectionStringParser.Password ?? string.Empty,
                ClientName = "SharpCoreDB.Client",
                ClientVersion = "1.5.0",
            }, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (connectResponse.Status != ConnectionStatus.Success)
            {
                throw new InvalidOperationException($"Connection rejected by server: {connectResponse.Status}");
            }

            SessionId = connectResponse.SessionId;
            ServerVersion = connectResponse.ServerVersion;

            lock (_stateLock)
            {
                _state = ConnectionState.Open;
            }
        }
        catch (InvalidOperationException)
        {
            lock (_stateLock)
            {
                _state = ConnectionState.Closed;
            }

            await CleanupResourcesAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Closes the connection.
    /// </summary>
    public async Task CloseAsync()
    {
        lock (_stateLock)
        {
            if (_state == ConnectionState.Closed)
            {
                return;
            }

            _state = ConnectionState.Closed;
        }

        await CleanupResourcesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a new command for this connection.
    /// </summary>
    public SharpCoreDBCommand CreateCommand()
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Connection not open");
        }

        return new SharpCoreDBCommand(this, _client);
    }

    /// <summary>
    /// Begins a new transaction.
    /// </summary>
    public async Task<SharpCoreDBTransaction> BeginTransactionAsync(
        SharpCoreDB.Server.Protocol.IsolationLevel isolationLevel = SharpCoreDB.Server.Protocol.IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        if (_state != ConnectionState.Open)
        {
            throw new InvalidOperationException("Connection is not open");
        }

        if (_client is null)
        {
            throw new InvalidOperationException("Client not initialized");
        }

        var txRequest = new BeginTxRequest
        {
            SessionId = SessionId ?? throw new InvalidOperationException("Connection not open"),
            IsolationLevel = isolationLevel,
            TimeoutMs = 300000,
        };

        var response = await _client.BeginTransactionAsync(txRequest, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new SharpCoreDBTransaction(this, _client, response.TransactionId);
    }

    private async Task CleanupResourcesAsync()
    {
        if (_channel is not null)
        {
            _channel.Dispose();
            _channel = null;
        }

        _client = null;
        SessionId = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Connection state enumeration.
/// </summary>
public enum ConnectionState
{
    Closed,
    Connecting,
    Open,
    Executing,
    Fetching,
}

/// <summary>
/// Parses SharpCoreDB connection strings.
/// C# 15: Primary constructor for immutable parser.
/// </summary>
public sealed class ConnectionStringParser(string connectionString)
{
    private readonly Dictionary<string, string> _parameters = ParseConnectionString(connectionString);

    /// <summary>Gets the server address (e.g., https://localhost:5001).</summary>
    public string ServerAddress
    {
        get
        {
            var server = GetParameter("Server", "localhost");
            var port = GetParameter("Port", "5001");
            var useSsl = GetParameter("SSL", "true").Equals("true", StringComparison.OrdinalIgnoreCase);
            var protocol = useSsl ? "https" : "http";
            return $"{protocol}://{server}:{port}";
        }
    }

    /// <summary>Prefer HTTP/3 transport for gRPC when available.</summary>
    public bool PreferHttp3 => GetParameter("PreferHttp3", "true").Equals("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets the database name.</summary>
    public string? Database => GetParameter("Database");

    /// <summary>Gets the username.</summary>
    public string? Username => GetParameter("Username");

    /// <summary>Gets the password.</summary>
    public string? Password => GetParameter("Password");

    private string GetParameter(string key, string defaultValue)
    {
        return _parameters.TryGetValue(key, out var value) ? value : defaultValue;
    }

    private string? GetParameter(string key)
    {
        return _parameters.TryGetValue(key, out var value) ? value : null;
    }

    private static Dictionary<string, string> ParseConnectionString(string connectionString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var keyValue = part.Split('=', 2);
            if (keyValue.Length == 2)
            {
                result[keyValue[0].Trim()] = keyValue[1].Trim();
            }
        }

        return result;
    }
}
