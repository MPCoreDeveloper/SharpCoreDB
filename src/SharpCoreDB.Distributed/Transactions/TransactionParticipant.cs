// <copyright file="TransactionParticipant.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using SharpCoreDB.Interfaces;
using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Distributed.Transactions;

/// <summary>
/// Concrete implementation of ITransactionParticipant for SharpCoreDB instances.
/// Wraps a database instance to participate in distributed transactions.
/// C# 14: Primary constructors, async patterns.
/// </summary>
public sealed class TransactionParticipant : ITransactionParticipant
{
    private readonly IDatabase _database;
    private readonly ILogger<TransactionParticipant>? _logger;

    private readonly Dictionary<string, TransactionState> _transactionStates = [];
    private readonly Lock _stateLock = new();

    /// <summary>Gets the participant identifier.</summary>
    public string ParticipantId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionParticipant"/> class.
    /// </summary>
    /// <param name="participantId">The unique participant identifier.</param>
    /// <param name="database">The SharpCoreDB database instance.</param>
    /// <param name="logger">Optional logger.</param>
    public TransactionParticipant(string participantId, IDatabase database, ILogger<TransactionParticipant>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(participantId);
        ArgumentNullException.ThrowIfNull(database);

        ParticipantId = participantId;
        _database = database;
        _logger = logger;
    }

    /// <summary>
    /// Prepares the participant for transaction commit.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The participant's vote.</returns>
    public async Task<Vote> PrepareAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        _logger?.LogInformation("Participant {ParticipantId} preparing for transaction {TransactionId}",
            ParticipantId, transactionId);

        // In SharpCoreDB, we don't have explicit prepare phase for single-node transactions
        // For distributed transactions, we would need to implement a prepare log or similar
        // For now, we'll simulate the prepare phase

        var state = new TransactionState(transactionId, DateTimeOffset.UtcNow);
        lock (_stateLock)
        {
            _transactionStates[transactionId] = state;
        }

        try
        {
            // Simulate preparation work (e.g., validate constraints, check locks)
            await Task.Delay(50, cancellationToken).ConfigureAwait(false); // Simulate work

            // Check if preparation succeeded (in real implementation, check for conflicts)
            var canCommit = await ValidateTransactionAsync(transactionId, cancellationToken).ConfigureAwait(false);

            state.Vote = canCommit ? Vote.Prepared : Vote.Abort;
            state.Phase = TransactionPhase.Prepared;

            _logger?.LogInformation("Participant {ParticipantId} voted {Vote} for transaction {TransactionId}",
                ParticipantId, state.Vote, transactionId);

            return state.Vote;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Participant {ParticipantId} failed to prepare transaction {TransactionId}",
                ParticipantId, transactionId);

            state.Vote = Vote.Abort;
            state.Phase = TransactionPhase.Failed;
            return Vote.Abort;
        }
    }

    /// <summary>
    /// Commits the participant's changes.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task CommitAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        _logger?.LogInformation("Participant {ParticipantId} committing transaction {TransactionId}",
            ParticipantId, transactionId);

        TransactionState? state;
        lock (_stateLock)
        {
            if (!_transactionStates.TryGetValue(transactionId, out state))
            {
                throw new InvalidOperationException($"Transaction {transactionId} not found");
            }
        }

        if (state.Phase != TransactionPhase.Prepared)
        {
            throw new InvalidOperationException($"Transaction {transactionId} is not prepared for commit");
        }

        try
        {
            // In SharpCoreDB, commit the transaction
            // For distributed transactions, this would involve making changes durable
            await PerformCommitAsync(transactionId, cancellationToken).ConfigureAwait(false);

            state.Phase = TransactionPhase.Committed;
            state.CommittedAt = DateTimeOffset.UtcNow;

            _logger?.LogInformation("Participant {ParticipantId} committed transaction {TransactionId}",
                ParticipantId, transactionId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Participant {ParticipantId} failed to commit transaction {TransactionId}",
                ParticipantId, transactionId);

            state.Phase = TransactionPhase.Failed;
            throw;
        }
    }

    /// <summary>
    /// Aborts the participant's changes.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task AbortAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        _logger?.LogInformation("Participant {ParticipantId} aborting transaction {TransactionId}",
            ParticipantId, transactionId);

        TransactionState? state;
        lock (_stateLock)
        {
            if (!_transactionStates.TryGetValue(transactionId, out state))
            {
                // Transaction not found, nothing to abort
                _logger?.LogWarning("Transaction {TransactionId} not found for abort", transactionId);
                return;
            }
        }

        try
        {
            // In SharpCoreDB, rollback the transaction
            await PerformAbortAsync(transactionId, cancellationToken).ConfigureAwait(false);

            state.Phase = TransactionPhase.Aborted;
            state.AbortedAt = DateTimeOffset.UtcNow;

            _logger?.LogInformation("Participant {ParticipantId} aborted transaction {TransactionId}",
                ParticipantId, transactionId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Participant {ParticipantId} failed to abort transaction {TransactionId}",
                ParticipantId, transactionId);

            state.Phase = TransactionPhase.Failed;
            throw;
        }
    }

    /// <summary>
    /// Gets the current phase of a transaction.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <returns>The transaction phase.</returns>
    public TransactionPhase GetTransactionPhase(string transactionId)
    {
        lock (_stateLock)
        {
            return _transactionStates.TryGetValue(transactionId, out var state)
                ? state.Phase
                : TransactionPhase.Unknown;
        }
    }

    /// <summary>
    /// Cleans up completed transactions.
    /// </summary>
    /// <param name="retentionPeriod">How long to retain completed transaction state.</param>
    public void CleanupCompletedTransactions(TimeSpan retentionPeriod)
    {
        var cutoff = DateTimeOffset.UtcNow - retentionPeriod;

        lock (_stateLock)
        {
            var toRemove = _transactionStates
                .Where(kvp =>
                    (kvp.Value.Phase == TransactionPhase.Committed && kvp.Value.CommittedAt < cutoff) ||
                    (kvp.Value.Phase == TransactionPhase.Aborted && kvp.Value.AbortedAt < cutoff) ||
                    (kvp.Value.Phase == TransactionPhase.Failed && kvp.Value.CreatedAt < cutoff))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var transactionId in toRemove)
            {
                _transactionStates.Remove(transactionId);
            }

            _logger?.LogDebug("Cleaned up {Count} completed transactions", toRemove.Count);
        }
    }

    /// <summary>
    /// Validates if a transaction can be committed.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the transaction can commit.</returns>
    private async Task<bool> ValidateTransactionAsync(string transactionId, CancellationToken cancellationToken)
    {
        // In a real implementation, this would:
        // - Check for constraint violations
        // - Validate foreign key constraints
        // - Check for deadlocks
        // - Verify resource availability

        // For now, simulate validation
        await Task.Delay(25, cancellationToken).ConfigureAwait(false);

        // Simulate random validation failure (5% chance)
        return Random.Shared.Next(100) >= 5;
    }

    /// <summary>
    /// Performs the actual commit operation.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task PerformCommitAsync(string transactionId, CancellationToken cancellationToken)
    {
        // In SharpCoreDB, this would involve:
        // - Writing commit record to WAL
        // - Making transaction changes durable
        // - Releasing locks

        // For now, simulate commit work
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);

        // Simulate random commit failure (2% chance)
        if (Random.Shared.Next(100) < 2)
        {
            throw new InvalidOperationException("Simulated commit failure");
        }
    }

    /// <summary>
    /// Performs the actual abort operation.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task PerformAbortAsync(string transactionId, CancellationToken cancellationToken)
    {
        // In SharpCoreDB, this would involve:
        // - Rolling back transaction changes
        // - Releasing locks
        // - Writing abort record to WAL

        // For now, simulate abort work
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Represents the state of a transaction at a participant.
/// </summary>
internal class TransactionState
{
    /// <summary>Gets the transaction identifier.</summary>
    public string TransactionId { get; }

    /// <summary>Gets the creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets or sets the current phase.</summary>
    public TransactionPhase Phase { get; set; }

    /// <summary>Gets or sets the participant's vote.</summary>
    public Vote Vote { get; set; }

    /// <summary>Gets or sets the commit timestamp.</summary>
    public DateTimeOffset? CommittedAt { get; set; }

    /// <summary>Gets or sets the abort timestamp.</summary>
    public DateTimeOffset? AbortedAt { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionState"/> class.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="createdAt">The creation timestamp.</param>
    public TransactionState(string transactionId, DateTimeOffset createdAt)
    {
        TransactionId = transactionId;
        CreatedAt = createdAt;
        Phase = TransactionPhase.Created;
        Vote = Vote.Unknown;
    }
}

/// <summary>
/// Transaction phases.
/// </summary>
public enum TransactionPhase
{
    /// <summary>Unknown phase.</summary>
    Unknown,

    /// <summary>Transaction created.</summary>
    Created,

    /// <summary>Transaction prepared.</summary>
    Prepared,

    /// <summary>Transaction committed.</summary>
    Committed,

    /// <summary>Transaction aborted.</summary>
    Aborted,

    /// <summary>Transaction failed.</summary>
    Failed
}
