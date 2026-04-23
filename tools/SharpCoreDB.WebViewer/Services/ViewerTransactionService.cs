using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using SharpCoreDB.Data.Provider;
using SharpCoreDB.WebViewer.Models;

namespace SharpCoreDB.WebViewer.Services;

/// <summary>
/// Manages active transaction contexts per browser session for local and server modes.
/// </summary>
public sealed class ViewerTransactionService(
    IHttpContextAccessor httpContextAccessor,
    IViewerConnectionService connectionService) : IViewerTransactionService
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly IViewerConnectionService _connectionService = connectionService;
    private readonly ConcurrentDictionary<string, TransactionContext> _contexts = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ViewerTransactionState? GetActiveTransaction()
    {
        var sessionId = GetSessionIdOrNull();
        if (sessionId is null)
        {
            return null;
        }

        return _contexts.TryGetValue(sessionId, out var context)
            ? context.State
            : null;
    }

    /// <inheritdoc />
    public async Task<ViewerTransactionState> BeginAsync(string? startedBy = null, CancellationToken cancellationToken = default)
    {
        var sessionId = GetSessionId();
        if (_contexts.ContainsKey(sessionId))
        {
            throw new InvalidOperationException("A transaction is already active for this session.");
        }

        var session = _connectionService.GetCurrentSession()
            ?? throw new InvalidOperationException("No active database connection is available.");

        TransactionContext context = session.ConnectionMode switch
        {
            ViewerConnectionMode.Server => await CreateServerContextAsync(session, startedBy, cancellationToken).ConfigureAwait(false),
            _ => await CreateLocalContextAsync(session, startedBy, cancellationToken).ConfigureAwait(false)
        };

        if (!_contexts.TryAdd(sessionId, context))
        {
            await DisposeContextAsync(context).ConfigureAwait(false);
            throw new InvalidOperationException("Failed to register active transaction context.");
        }

        return context.State;
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        var context = GetRequiredContext();

        if (context.LocalTransaction is not null)
        {
            context.LocalTransaction.Commit();
            await ClearAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (context.ServerTransaction is not null)
        {
            await context.ServerTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            await ClearAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException("No active transaction instance is available.");
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        var context = GetRequiredContext();

        if (context.LocalTransaction is not null)
        {
            context.LocalTransaction.Rollback();
            await ClearAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (context.ServerTransaction is not null)
        {
            await context.ServerTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            await ClearAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException("No active transaction instance is available.");
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sessionId = GetSessionIdOrNull();
        if (sessionId is null)
        {
            return;
        }

        if (_contexts.TryRemove(sessionId, out var context))
        {
            await DisposeContextAsync(context).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public bool TryGetLocalExecutionConnection(out SharpCoreDBConnection? connection)
    {
        var context = GetCurrentContextOrNull();
        connection = context?.LocalConnection;
        return connection is not null;
    }

    /// <inheritdoc />
    public bool TryGetServerExecutionConnection(out SharpCoreDB.Client.SharpCoreDBConnection? connection)
    {
        var context = GetCurrentContextOrNull();
        connection = context?.ServerConnection;
        return connection is not null;
    }

    private TransactionContext? GetCurrentContextOrNull()
    {
        var sessionId = GetSessionIdOrNull();
        if (sessionId is null)
        {
            return null;
        }

        return _contexts.TryGetValue(sessionId, out var context)
            ? context
            : null;
    }

    private TransactionContext GetRequiredContext()
    {
        var context = GetCurrentContextOrNull();
        return context ?? throw new InvalidOperationException("No active transaction exists for this session.");
    }

    private async Task<TransactionContext> CreateLocalContextAsync(
        ViewerSessionState session,
        string? startedBy,
        CancellationToken cancellationToken)
    {
        var connection = new SharpCoreDBConnection(_connectionService.BuildLocalConnectionString(session));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var transaction = (SharpCoreDBTransaction)connection.BeginTransaction();
        var state = new ViewerTransactionState
        {
            ConnectionMode = ViewerConnectionMode.Local,
            StartedAtUtc = DateTimeOffset.UtcNow,
            StartedBy = startedBy
        };

        return new TransactionContext
        {
            State = state,
            LocalConnection = connection,
            LocalTransaction = transaction
        };
    }

    private async Task<TransactionContext> CreateServerContextAsync(
        ViewerSessionState session,
        string? startedBy,
        CancellationToken cancellationToken)
    {
        var connection = new SharpCoreDB.Client.SharpCoreDBConnection(_connectionService.BuildServerConnectionString(session));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var transaction = await connection.BeginTransactionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var state = new ViewerTransactionState
        {
            ConnectionMode = ViewerConnectionMode.Server,
            ServerTransactionId = transaction.TransactionId,
            StartedAtUtc = DateTimeOffset.UtcNow,
            StartedBy = startedBy
        };

        return new TransactionContext
        {
            State = state,
            ServerConnection = connection,
            ServerTransaction = transaction
        };
    }

    private static async Task DisposeContextAsync(TransactionContext context)
    {
        if (context.LocalTransaction is not null)
        {
            context.LocalTransaction.Dispose();
        }

        if (context.LocalConnection is not null)
        {
            await context.LocalConnection.DisposeAsync().ConfigureAwait(false);
        }

        if (context.ServerTransaction is not null)
        {
            await context.ServerTransaction.DisposeAsync().ConfigureAwait(false);
        }

        if (context.ServerConnection is not null)
        {
            await context.ServerConnection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private string GetSessionId()
    {
        var session = _httpContextAccessor.HttpContext?.Session
            ?? throw new InvalidOperationException("The current HTTP session is not available.");

        return session.Id;
    }

    private string? GetSessionIdOrNull() => _httpContextAccessor.HttpContext?.Session?.Id;

    private sealed class TransactionContext
    {
        public required ViewerTransactionState State { get; init; }

        public SharpCoreDBConnection? LocalConnection { get; init; }

        public SharpCoreDBTransaction? LocalTransaction { get; init; }

        public SharpCoreDB.Client.SharpCoreDBConnection? ServerConnection { get; init; }

        public SharpCoreDB.Client.SharpCoreDBTransaction? ServerTransaction { get; init; }
    }
}
