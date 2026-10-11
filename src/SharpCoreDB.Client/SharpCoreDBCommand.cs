// <copyright file="SharpCoreDBCommand.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Google.Protobuf;
using SharpCoreDB.Server.Protocol;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCoreDB.Client;

/// <summary>
/// ADO.NET-like command for executing queries against SharpCoreDB server.
/// C# 15: Uses primary constructor for dependencies.
/// </summary>
public sealed class SharpCoreDBCommand(
    SharpCoreDBConnection connection,
    DatabaseService.DatabaseServiceClient client)
{
    private string? _commandText;
    private readonly Dictionary<string, object?> _parameters = [];
    private int _commandTimeout = 30000; // 30 seconds default

    /// <summary>
    /// Gets or sets the SQL command text.
    /// </summary>
    public string CommandText
    {
        get => _commandText ?? throw new InvalidOperationException("CommandText is not set");
        set => _commandText = value;
    }

    /// <summary>
    /// Gets or sets the command timeout (milliseconds).
    /// </summary>
    public int CommandTimeout
    {
        get => _commandTimeout;
        set => _commandTimeout = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>
    /// Gets the parameter collection.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Parameters => _parameters;

    /// <summary>
    /// Adds a parameter to the command.
    /// </summary>
    public void AddParameter(string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        _parameters[name] = value;
    }

    /// <summary>
    /// Clears all parameters.
    /// </summary>
    public void ClearParameters()
    {
        _parameters.Clear();
    }

    /// <summary>
    /// Executes a query that returns a result set.
    /// </summary>
    public async Task<SharpCoreDBDataReader> ExecuteReaderAsync(CancellationToken cancellationToken = default)
    {
        var request = new QueryRequest
        {
            SessionId = connection.SessionId ?? throw new InvalidOperationException("Connection not open"),
            Sql = CommandText,
            TimeoutMs = CommandTimeout,
            Options = new QueryOptions
            {
                Streaming = true,
                FetchSize = 1000
            }
        };

        // Convert parameters
        foreach (var (name, value) in _parameters)
        {
            request.Parameters[name] = ConvertParameter(value);
        }

        var call = client.ExecuteQuery(request);
        return new SharpCoreDBDataReader(call.ResponseStream, cancellationToken);
    }

    /// <summary>
    /// Executes a query that does not return a result set.
    /// </summary>
    public async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
    {
        var request = new NonQueryRequest
        {
            SessionId = connection.SessionId ?? throw new InvalidOperationException("Connection not open"),
            Sql = CommandText,
            TimeoutMs = CommandTimeout
        };

        // Convert parameters
        foreach (var (name, value) in _parameters)
        {
            request.Parameters[name] = ConvertParameter(value);
        }

        var response = await client.ExecuteNonQueryAsync(request, cancellationToken: cancellationToken);
        return (int)response.RowsAffected;
    }

    /// <summary>
    /// Executes a query that returns a single scalar value.
    /// </summary>
    public async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken = default)
    {
        await using var reader = await ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return reader.GetValue(0);
        }
        return null;
    }

    /// <summary>
    /// Begins a transaction.
    /// </summary>
    public async Task<string> BeginTransactionAsync(SharpCoreDB.Server.Protocol.IsolationLevel isolationLevel = SharpCoreDB.Server.Protocol.IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        var request = new BeginTxRequest
        {
            SessionId = connection.SessionId ?? throw new InvalidOperationException("Connection not open"),
            IsolationLevel = isolationLevel,
            TimeoutMs = CommandTimeout
        };

        var response = await client.BeginTransactionAsync(request, cancellationToken: cancellationToken);
        return response.TransactionId;
    }

    /// <summary>
    /// Commits a transaction.
    /// </summary>
    public async Task CommitTransactionAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        var request = new CommitTxRequest
        {
            SessionId = connection.SessionId ?? throw new InvalidOperationException("Connection not open"),
            TransactionId = transactionId
        };

        await client.CommitTransactionAsync(request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Rolls back a transaction.
    /// </summary>
    public async Task RollbackTransactionAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        var request = new RollbackTxRequest
        {
            SessionId = connection.SessionId ?? throw new InvalidOperationException("Connection not open"),
            TransactionId = transactionId
        };

        await client.RollbackTransactionAsync(request, cancellationToken: cancellationToken);
    }

    private static ParameterValue ConvertParameter(object? value)
    {
        var paramValue = new ParameterValue();

        switch (value)
        {
            case null:
                // null is represented by empty oneof
                break;
            case int intValue:
                paramValue.IntValue = intValue;
                break;
            case long longValue:
                paramValue.LongValue = longValue;
                break;
            case double doubleValue:
                paramValue.DoubleValue = doubleValue;
                break;
            case string ulidString when ulidString.Length == 26 && ulidString.All(c => char.IsLetterOrDigit(c)):
                paramValue.UlidValue = ulidString;
                break;
            case string stringValue:
                paramValue.StringValue = stringValue;
                break;
            case byte[] bytesValue:
                paramValue.BytesValue = ByteString.CopyFrom(bytesValue);
                break;
            case bool boolValue:
                paramValue.BoolValue = boolValue;
                break;
            case DateTime dateTimeValue:
                paramValue.TimestampValue = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(dateTimeValue.ToUniversalTime());
                break;
            case Guid guidValue:
                paramValue.GuidValue = guidValue.ToString();
                break;
            case float[] vectorValue:
                paramValue.VectorValue = new VectorValue();
                paramValue.VectorValue.Values.AddRange(vectorValue);
                break;
            default:
                paramValue.StringValue = value.ToString() ?? "";
                break;
        }

        return paramValue;
    }
}

/// <summary>
/// Transaction for SharpCoreDB.
/// C# 15: Primary constructor for immutable transaction handle.
/// </summary>
public sealed class SharpCoreDBTransaction(
    SharpCoreDBConnection connection,
    DatabaseService.DatabaseServiceClient client,
    string transactionId) : IAsyncDisposable
{
    /// <summary>Gets the transaction ID.</summary>
    public string TransactionId { get; } = transactionId;

    /// <summary>Commits the transaction.</summary>
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        var request = new CommitTxRequest { SessionId = connection.SessionId ?? throw new InvalidOperationException("Connection not open"), TransactionId = TransactionId };
        await client.CommitTransactionAsync(request, cancellationToken: cancellationToken);
    }

    /// <summary>Rolls back the transaction.</summary>
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        var request = new RollbackTxRequest { SessionId = connection.SessionId ?? throw new InvalidOperationException("Connection not open"), TransactionId = TransactionId };
        await client.RollbackTransactionAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Auto-rollback if not committed
        try
        {
            await RollbackAsync();
        }
        catch
        {
            // Ignore errors on dispose
        }
    }
}
