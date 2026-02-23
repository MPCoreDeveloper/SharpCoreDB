// <copyright file="DistributedTransactionManager.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SharpCoreDB.Distributed.Sharding;

namespace SharpCoreDB.Distributed.Transactions;

/// <summary>
/// Manages distributed transactions across multiple database nodes/shards.
/// Implements two-phase commit protocol for atomic distributed operations.
/// C# 14: Primary constructors, Channel<T> for coordination, async patterns.
/// </summary>
public sealed class DistributedTransactionManager : IAsyncDisposable
{
    private readonly ShardManager _shardManager;
    private readonly ILogger<DistributedTransactionManager>? _logger;

    private readonly Dictionary<string, DistributedTransaction> _activeTransactions = [];
    private readonly Lock _transactionLock = new();

    private readonly Channel<TransactionCommand> _commandChannel = Channel.CreateBounded<TransactionCommand>(1000);
    private readonly CancellationTokenSource _cts = new();

    private Task? _commandProcessorTask;
    private bool _isRunning;

    /// <summary>Gets the number of active distributed transactions.</summary>
    public int ActiveTransactionCount => _activeTransactions.Count;

    /// <summary>Gets all active transaction identifiers.</summary>
    public IReadOnlyCollection<string> ActiveTransactionIds => [.. _activeTransactions.Keys];

    /// <summary>
    /// Initializes a new instance of the <see cref="DistributedTransactionManager"/> class.
    /// </summary>
    /// <param name="shardManager">The shard manager for node coordination.</param>
    /// <param name="logger">Optional logger for transaction events.</param>
    public DistributedTransactionManager(ShardManager shardManager, ILogger<DistributedTransactionManager>? logger = null)
    {
        _shardManager = shardManager ?? throw new ArgumentNullException(nameof(shardManager));
        _logger = logger;
    }

    /// <summary>
    /// Starts the distributed transaction manager.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;
        _commandProcessorTask = ProcessCommandsAsync(_cts.Token);

        _logger?.LogInformation("Distributed transaction manager started");
    }

    /// <summary>
    /// Stops the distributed transaction manager.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        _cts.Cancel();

        if (_commandProcessorTask is not null)
        {
            try
            {
                await _commandProcessorTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }

        // Abort any remaining transactions
        await AbortAllTransactionsAsync(cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("Distributed transaction manager stopped");
    }

    /// <summary>
    /// Begins a new distributed transaction.
    /// </summary>
    /// <param name="transactionId">Unique transaction identifier.</param>
    /// <param name="participantShards">List of shard IDs participating in the transaction.</param>
    /// <param name="isolationLevel">Transaction isolation level.</param>
    /// <param name="timeout">Transaction timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task BeginTransactionAsync(
        string transactionId,
        IReadOnlyList<string> participantShards,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentNullException.ThrowIfNull(participantShards);

        if (participantShards.Count == 0)
        {
            throw new ArgumentException("At least one participant shard must be specified", nameof(participantShards));
        }

        timeout ??= TimeSpan.FromMinutes(5); // Default 5 minute timeout

        var transaction = new DistributedTransaction
        {
            TransactionId = transactionId,
            ParticipantShardIds = [.. participantShards],
            IsolationLevel = isolationLevel,
            Timeout = timeout.Value,
            StartTime = DateTimeOffset.UtcNow,
            Status = TransactionStatus.Active
        };

        lock (_transactionLock)
        {
            if (_activeTransactions.ContainsKey(transactionId))
            {
                throw new InvalidOperationException($"Transaction '{transactionId}' already exists");
            }

            _activeTransactions[transactionId] = transaction;
        }

        // Send begin command to participants
        await _commandChannel.Writer.WriteAsync(
            new TransactionCommand(TransactionCommandType.Begin, transactionId, participantShards),
            cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("Begun distributed transaction {TransactionId} with {ParticipantCount} participants",
            transactionId, participantShards.Count);
    }

    /// <summary>
    /// Prepares a distributed transaction for commit (Phase 1 of 2PC).
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task PrepareTransactionAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        DistributedTransaction transaction;
        lock (_transactionLock)
        {
            if (!_activeTransactions.TryGetValue(transactionId, out transaction!))
            {
                throw new InvalidOperationException($"Transaction '{transactionId}' not found");
            }

            if (transaction.Status != TransactionStatus.Active)
            {
                throw new InvalidOperationException($"Transaction '{transactionId}' is not in active state");
            }

            transaction.Status = TransactionStatus.Preparing;
        }

        // Send prepare command to all participants
        await _commandChannel.Writer.WriteAsync(
            new TransactionCommand(TransactionCommandType.Prepare, transactionId, transaction.ParticipantShardIds),
            cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("Prepared distributed transaction {TransactionId} for commit", transactionId);
    }

    /// <summary>
    /// Commits a prepared distributed transaction (Phase 2 of 2PC).
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task CommitTransactionAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        DistributedTransaction transaction;
        lock (_transactionLock)
        {
            if (!_activeTransactions.TryGetValue(transactionId, out transaction!))
            {
                throw new InvalidOperationException($"Transaction '{transactionId}' not found");
            }

            if (transaction.Status != TransactionStatus.Prepared)
            {
                throw new InvalidOperationException($"Transaction '{transactionId}' is not prepared for commit");
            }

            transaction.Status = TransactionStatus.Committing;
        }

        // Send commit command to all participants
        await _commandChannel.Writer.WriteAsync(
            new TransactionCommand(TransactionCommandType.Commit, transactionId, transaction.ParticipantShardIds),
            cancellationToken).ConfigureAwait(false);

        // Wait for all participants to acknowledge
        // In a real implementation, this would wait for responses

        lock (_transactionLock)
        {
            transaction.Status = TransactionStatus.Committed;
            transaction.EndTime = DateTimeOffset.UtcNow;
        }

        _logger?.LogInformation("Committed distributed transaction {TransactionId}", transactionId);
    }

    /// <summary>
    /// Aborts a distributed transaction.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task AbortTransactionAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        DistributedTransaction transaction;
        lock (_transactionLock)
        {
            if (!_activeTransactions.TryGetValue(transactionId, out transaction!))
            {
                throw new InvalidOperationException($"Transaction '{transactionId}' not found");
            }

            transaction.Status = TransactionStatus.Aborting;
        }

        // Send abort command to all participants
        await _commandChannel.Writer.WriteAsync(
            new TransactionCommand(TransactionCommandType.Abort, transactionId, transaction.ParticipantShardIds),
            cancellationToken).ConfigureAwait(false);

        lock (_transactionLock)
        {
            transaction.Status = TransactionStatus.Aborted;
            transaction.EndTime = DateTimeOffset.UtcNow;
        }

        _logger?.LogInformation("Aborted distributed transaction {TransactionId}", transactionId);
    }

    /// <summary>
    /// Gets the status of a distributed transaction.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <returns>The transaction status.</returns>
    public TransactionStatus GetTransactionStatus(string transactionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        lock (_transactionLock)
        {
            return _activeTransactions.TryGetValue(transactionId, out var transaction)
                ? transaction.Status
                : TransactionStatus.Unknown;
        }
    }

    /// <summary>
    /// Gets detailed information about a distributed transaction.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <returns>The transaction information, or null if not found.</returns>
    public DistributedTransaction? GetTransactionInfo(string transactionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        lock (_transactionLock)
        {
            return _activeTransactions.TryGetValue(transactionId, out var transaction)
                ? transaction
                : null;
        }
    }

    /// <summary>
    /// Processes transaction commands asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessCommandsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var command in _commandChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await ProcessCommandAsync(command, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error processing transaction command {CommandType} for transaction {TransactionId}",
                        command.CommandType, command.TransactionId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Fatal error in transaction command processor");
        }
    }

    /// <summary>
    /// Processes a single transaction command.
    /// </summary>
    /// <param name="command">The command to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessCommandAsync(TransactionCommand command, CancellationToken cancellationToken)
    {
        // In a real implementation, this would coordinate with actual database instances
        // For now, we'll simulate the coordination

        _logger?.LogDebug("Processing {CommandType} command for transaction {TransactionId} on {ParticipantCount} shards",
            command.CommandType, command.TransactionId, command.ParticipantShardIds.Count);

        // Simulate network calls to participants
        foreach (var shardId in command.ParticipantShardIds)
        {
            if (!_shardManager.ShardIds.Contains(shardId))
            {
                _logger?.LogWarning("Shard {ShardId} not found in shard manager", shardId);
                continue;
            }

            // In real implementation: send command to shard via network
            await Task.Delay(10, cancellationToken).ConfigureAwait(false); // Simulate network latency
        }
    }

    /// <summary>
    /// Aborts all active transactions.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task AbortAllTransactionsAsync(CancellationToken cancellationToken)
    {
        List<string> transactionIds;
        lock (_transactionLock)
        {
            transactionIds = [.. _activeTransactions.Keys];
        }

        var abortTasks = transactionIds.Select(id => AbortTransactionAsync(id, cancellationToken)).ToList();
        await Task.WhenAll(abortTasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes the distributed transaction manager.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts.Dispose();
        _commandChannel.Writer.Complete();
    }
}

/// <summary>
/// Represents a distributed transaction.
/// </summary>
public class DistributedTransaction
{
    /// <summary>Gets the unique transaction identifier.</summary>
    public required string TransactionId { get; init; }

    /// <summary>Gets the list of participating shard IDs.</summary>
    public required IReadOnlyList<string> ParticipantShardIds { get; init; }

    /// <summary>Gets the transaction isolation level.</summary>
    public IsolationLevel IsolationLevel { get; init; }

    /// <summary>Gets the transaction timeout.</summary>
    public TimeSpan Timeout { get; init; }

    /// <summary>Gets the transaction start time.</summary>
    public DateTimeOffset StartTime { get; init; }

    /// <summary>Gets or sets the transaction end time.</summary>
    public DateTimeOffset? EndTime { get; set; }

    /// <summary>Gets or sets the transaction status.</summary>
    public TransactionStatus Status { get; set; }
}

/// <summary>
/// Transaction command types.
/// </summary>
public enum TransactionCommandType
{
    /// <summary>Begin transaction.</summary>
    Begin,

    /// <summary>Prepare for commit.</summary>
    Prepare,

    /// <summary>Commit transaction.</summary>
    Commit,

    /// <summary>Abort transaction.</summary>
    Abort
}

/// <summary>
/// Transaction status.
/// </summary>
public enum TransactionStatus
{
    /// <summary>Unknown transaction.</summary>
    Unknown,

    /// <summary>Transaction is active.</summary>
    Active,

    /// <summary>Transaction is being prepared.</summary>
    Preparing,

    /// <summary>Transaction is prepared for commit.</summary>
    Prepared,

    /// <summary>Transaction is committing.</summary>
    Committing,

    /// <summary>Transaction committed successfully.</summary>
    Committed,

    /// <summary>Transaction is aborting.</summary>
    Aborting,

    /// <summary>Transaction aborted.</summary>
    Aborted
}

/// <summary>
/// Transaction isolation levels.
/// </summary>
public enum IsolationLevel
{
    /// <summary>Read uncommitted isolation.</summary>
    ReadUncommitted,

    /// <summary>Read committed isolation.</summary>
    ReadCommitted,

    /// <summary>Repeatable read isolation.</summary>
    RepeatableRead,

    /// <summary>Serializable isolation.</summary>
    Serializable
}

/// <summary>
/// Represents a transaction command.
/// </summary>
internal class TransactionCommand
{
    /// <summary>Gets the command type.</summary>
    public TransactionCommandType CommandType { get; }

    /// <summary>Gets the transaction identifier.</summary>
    public string TransactionId { get; }

    /// <summary>Gets the participant shard IDs.</summary>
    public IReadOnlyList<string> ParticipantShardIds { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionCommand"/> class.
    /// </summary>
    /// <param name="commandType">The command type.</param>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="participantShardIds">The participant shard IDs.</param>
    public TransactionCommand(TransactionCommandType commandType, string transactionId, IReadOnlyList<string> participantShardIds)
    {
        CommandType = commandType;
        TransactionId = transactionId;
        ParticipantShardIds = participantShardIds;
    }
}
