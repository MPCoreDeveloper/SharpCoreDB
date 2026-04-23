using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SharpCoreDB.Client;
using SharpCoreDB.Data.Provider;
using SharpCoreDB.Interfaces;
using SharpCoreDB.WebViewer.Models;

namespace SharpCoreDB.WebViewer.Services;

/// <summary>
/// Stores active viewer connection state in the current ASP.NET Core session.
/// </summary>
public sealed class ViewerConnectionService(DatabaseFactory databaseFactory, IHttpContextAccessor httpContextAccessor) : IViewerConnectionService
{
    private const string SessionKey = "SharpCoreDB.WebViewer.ActiveConnection";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly DatabaseFactory _databaseFactory = databaseFactory;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    /// <inheritdoc />
    public ViewerSessionState? GetCurrentSession()
    {
        var session = GetSession();
        var serializedState = session.GetString(SessionKey);
        return string.IsNullOrWhiteSpace(serializedState)
            ? null
            : JsonSerializer.Deserialize<ViewerSessionState>(serializedState, JsonOptions);
    }

    /// <inheritdoc />
    public async Task<ViewerSessionState> ConnectAsync(ConnectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var sessionState = request.ConnectionMode switch
        {
            ViewerConnectionMode.Server => CreateServerSession(request),
            _ => CreateLocalSession(request)
        };

        await ValidateConnectivityAsync(sessionState, cancellationToken).ConfigureAwait(false);

        var session = GetSession();
        session.SetString(SessionKey, JsonSerializer.Serialize(sessionState, JsonOptions));

        return sessionState;
    }

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = GetSession();
        session.Remove(SessionKey);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public string BuildLocalConnectionString(ViewerSessionState session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.ConnectionMode != ViewerConnectionMode.Local)
        {
            throw new InvalidOperationException("The active session is not configured for local connection mode.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(session.LocalDatabasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.Password);

        var builder = new SharpCoreDBConnectionStringBuilder
        {
            Path = session.LocalDatabasePath,
            Password = session.Password,
            ReadOnly = session.LocalReadOnly,
            Cache = "Private"
        };

        return builder.ConnectionString;
    }

    /// <inheritdoc />
    public string BuildServerConnectionString(ViewerSessionState session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.ConnectionMode != ViewerConnectionMode.Server)
        {
            throw new InvalidOperationException("The active session is not configured for server connection mode.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(session.ServerHost);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.ServerDatabase);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.ServerUsername);

        var parts = new List<string>
        {
            $"Server={session.ServerHost}",
            $"Port={session.ServerPort}",
            $"Database={session.ServerDatabase}",
            $"SSL={session.ServerUseSsl}",
            $"PreferHttp3={session.ServerPreferHttp3}",
            $"Username={session.ServerUsername}",
            $"Password={session.Password}"
        };

        return string.Join(';', parts);
    }

    private async Task ValidateConnectivityAsync(ViewerSessionState sessionState, CancellationToken cancellationToken)
    {
        switch (sessionState.ConnectionMode)
        {
            case ViewerConnectionMode.Server:
                await ProbeServerConnectionAsync(sessionState, cancellationToken).ConfigureAwait(false);
                break;
            case ViewerConnectionMode.Local:
                await ProbeLocalConnectionAsync(sessionState, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(sessionState.ConnectionMode), sessionState.ConnectionMode, "Unsupported connection mode.");
        }
    }

    private async Task ProbeLocalConnectionAsync(ViewerSessionState sessionState, CancellationToken cancellationToken)
    {
        await using var database = await OpenLocalDatabaseAsync(sessionState, cancellationToken).ConfigureAwait(false);
        if (database is not IMetadataProvider metadataProvider)
        {
            throw new InvalidOperationException("The selected local database does not support metadata discovery.");
        }

        _ = metadataProvider.GetTables();
    }

    private async Task ProbeServerConnectionAsync(ViewerSessionState sessionState, CancellationToken cancellationToken)
    {
        await using var connection = new SharpCoreDB.Client.SharpCoreDBConnection(BuildServerConnectionString(sessionState));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IDatabase> OpenLocalDatabaseAsync(ViewerSessionState sessionState, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var options = CreateDatabaseOptions(sessionState.LocalStorageMode, sessionState.LocalReadOnly);
            return _databaseFactory.CreateWithOptions(sessionState.LocalDatabasePath!, sessionState.Password, options);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static ViewerSessionState CreateLocalSession(ConnectionRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LocalDatabasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Password);

        var normalizedPath = NormalizePath(request.LocalDatabasePath);

        return new ViewerSessionState
        {
            Name = GetConnectionName(request.Name, request.ConnectionMode, normalizedPath, null, null, null),
            ConnectionMode = ViewerConnectionMode.Local,
            LocalDatabasePath = normalizedPath,
            LocalStorageMode = request.LocalStorageMode,
            LocalReadOnly = request.LocalReadOnly,
            Password = request.Password,
            ConnectedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static ViewerSessionState CreateServerSession(ConnectionRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ServerHost);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ServerDatabase);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ServerUsername);

        return new ViewerSessionState
        {
            Name = GetConnectionName(request.Name, request.ConnectionMode, null, request.ServerHost, request.ServerPort, request.ServerDatabase),
            ConnectionMode = ViewerConnectionMode.Server,
            ServerHost = request.ServerHost.Trim(),
            ServerPort = request.ServerPort,
            ServerUseSsl = request.ServerUseSsl,
            ServerPreferHttp3 = request.ServerPreferHttp3,
            ServerDatabase = request.ServerDatabase.Trim(),
            ServerUsername = request.ServerUsername.Trim(),
            Password = request.Password,
            ConnectedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static DatabaseOptions CreateDatabaseOptions(DatabaseStorageMode storageMode, bool readOnly)
    {
        var options = storageMode switch
        {
            DatabaseStorageMode.SingleFile => DatabaseOptions.CreateSingleFileDefault(),
            _ => DatabaseOptions.CreateDirectoryDefault()
        };

        options.IsReadOnly = readOnly;
        return options;
    }

    private static string NormalizePath(string databasePath) => Path.IsPathRooted(databasePath)
        ? Path.GetFullPath(databasePath)
        : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, databasePath));

    private static string GetConnectionName(
        string? name,
        ViewerConnectionMode connectionMode,
        string? localDatabasePath,
        string? serverHost,
        int? serverPort,
        string? serverDatabase)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        if (connectionMode == ViewerConnectionMode.Server)
        {
            return $"{serverHost}:{serverPort}/{serverDatabase}";
        }

        return Path.GetFileName(localDatabasePath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            ?? localDatabasePath
            ?? "local-database";
    }

    private ISession GetSession() => _httpContextAccessor.HttpContext?.Session
        ?? throw new InvalidOperationException("The current HTTP session is not available.");
}
