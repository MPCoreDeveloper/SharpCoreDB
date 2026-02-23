// <copyright file="TwoPhaseCommitProtocol.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Distributed.Transactions;

/// <summary>
/// Implements the Two-Phase Commit (2PC) protocol for distributed transactions.
/// Handles prepare, commit, and abort phases with proper error handling and recovery.
/// C# 14: Primary constructors, Channel<T> for message passing, async coordination.
/// </summary>
public sealed class TwoPhaseCommitProtocol : IAsyncDisposable
{
    private readonly ILogger<TwoPhaseCommitProtocol>? _logger;

    private readonly Channel<ProtocolMessage> _messageChannel = Channel.CreateBounded<ProtocolMessage>(1000);
    private readonly Dictionary<string, TransactionCoordinator> _coordinators = [];
    private readonly Lock _coordinatorLock = new();

    private readonly CancellationTokenSource _cts = new();
    private Task? _messageProcessorTask;
    private bool _isRunning;

    /// <summary>
    /// Initializes a new instance of the <see cref="TwoPhaseCommitProtocol"/> class.
    /// </summary>
    /// <param name="logger">Optional logger for protocol events.</param>
    public TwoPhaseCommitProtocol(ILogger<TwoPhaseCommitProtocol>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Starts the 2PC protocol processor.
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
        _messageProcessorTask = ProcessMessagesAsync(_cts.Token);

        _logger?.LogInformation("Two-phase commit protocol started");
    }

    /// <summary>
    /// Stops the 2PC protocol processor.
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

        if (_messageProcessorTask is not null)
        {
            try
            {
                await _messageProcessorTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }

        _logger?.LogInformation("Two-phase commit protocol stopped");
    }

    /// <summary>
    /// Initiates a two-phase commit for a distributed transaction.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="participants">The transaction participants.</param>
    /// <param name="timeout">The protocol timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The commit result.</returns>
    public async Task<CommitResult> ExecuteTwoPhaseCommitAsync(
        string transactionId,
        IReadOnlyList<ITransactionParticipant> participants,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentNullException.ThrowIfNull(participants);

        if (participants.Count == 0)
        {
            throw new ArgumentException("At least one participant required", nameof(participants));
        }

        var coordinator = new TransactionCoordinator(transactionId, participants, timeout, _logger);
        lock (_coordinatorLock)
        {
            _coordinators[transactionId] = coordinator;
        }

        try
        {
            _logger?.LogInformation("Starting 2PC for transaction {TransactionId} with {ParticipantCount} participants",
                transactionId, participants.Count);

            // Phase 1: Prepare
            var prepareResult = await coordinator.ExecutePreparePhaseAsync(cancellationToken).ConfigureAwait(false);
            if (prepareResult != PrepareResult.Success)
            {
                _logger?.LogWarning("Prepare phase failed for transaction {TransactionId}: {Result}", transactionId, prepareResult);

                // Phase 2: Abort
                await coordinator.ExecuteAbortPhaseAsync(cancellationToken).ConfigureAwait(false);
                return CommitResult.Aborted;
            }

            // Phase 2: Commit
            var commitResult = await coordinator.ExecuteCommitPhaseAsync(cancellationToken).ConfigureAwait(false);
            return commitResult == CommitResult.Success ? CommitResult.Success : CommitResult.Failed;
        }
        finally
        {
            lock (_coordinatorLock)
            {
                _coordinators.Remove(transactionId);
            }
        }
    }

    /// <summary>
    /// Processes protocol messages asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _messageChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await ProcessMessageAsync(message, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error processing protocol message for transaction {TransactionId}",
                        message.TransactionId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Fatal error in protocol message processor");
        }
    }

    /// <summary>
    /// Processes a protocol message.
    /// </summary>
    /// <param name="message">The message to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessMessageAsync(ProtocolMessage message, CancellationToken cancellationToken)
    {
        TransactionCoordinator? coordinator;
        lock (_coordinatorLock)
        {
            _coordinators.TryGetValue(message.TransactionId, out coordinator);
        }

        if (coordinator is null)
        {
            _logger?.LogWarning("Received message for unknown transaction {TransactionId}", message.TransactionId);
            return;
        }

        await coordinator.ProcessMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes the 2PC protocol.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts.Dispose();
        _messageChannel.Writer.Complete();
    }
}

/// <summary>
/// Coordinates a single distributed transaction through 2PC.
/// </summary>
internal sealed class TransactionCoordinator
{
    private readonly string _transactionId;
    private readonly IReadOnlyList<ITransactionParticipant> _participants;
    private readonly TimeSpan _timeout;
    private readonly ILogger? _logger;

    private readonly Dictionary<string, ParticipantState> _participantStates = [];
    private readonly Lock _stateLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionCoordinator"/> class.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="participants">The transaction participants.</param>
    /// <param name="timeout">The coordination timeout.</param>
    /// <param name="logger">Optional logger.</param>
    public TransactionCoordinator(
        string transactionId,
        IReadOnlyList<ITransactionParticipant> participants,
        TimeSpan timeout,
        ILogger? logger = null)
    {
        _transactionId = transactionId;
        _participants = participants;
        _timeout = timeout;
        _logger = logger;

        // Initialize participant states
        foreach (var participant in participants)
        {
            _participantStates[participant.ParticipantId] = new ParticipantState(participant.ParticipantId);
        }
    }

    /// <summary>
    /// Executes the prepare phase of 2PC.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The prepare result.</returns>
    public async Task<PrepareResult> ExecutePreparePhaseAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Starting prepare phase for transaction {TransactionId}", _transactionId);

        // Send prepare requests to all participants
        var prepareTasks = _participants.Select(p => SendPrepareRequestAsync(p, cancellationToken)).ToList();

        // Wait for all responses with timeout
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        try
        {
            await Task.WhenAll(prepareTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _logger?.LogWarning("Prepare phase timed out for transaction {TransactionId}", _transactionId);
            return PrepareResult.Timeout;
        }

        // Check if all participants voted to commit
        lock (_stateLock)
        {
            var allPrepared = _participantStates.Values.All(s => s.Vote == Vote.Prepared);
            var anyFailed = _participantStates.Values.Any(s => s.Vote == Vote.Abort);

            if (allPrepared)
            {
                _logger?.LogInformation("All participants prepared for transaction {TransactionId}", _transactionId);
                return PrepareResult.Success;
            }
            else if (anyFailed)
            {
                _logger?.LogWarning("Some participants aborted transaction {TransactionId}", _transactionId);
                return PrepareResult.Abort;
            }
            else
            {
                _logger?.LogWarning("Prepare phase incomplete for transaction {TransactionId}", _transactionId);
                return PrepareResult.Incomplete;
            }
        }
    }

    /// <summary>
    /// Executes the commit phase of 2PC.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The commit result.</returns>
    public async Task<CommitResult> ExecuteCommitPhaseAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Starting commit phase for transaction {TransactionId}", _transactionId);

        // Send commit requests to all participants
        var commitTasks = _participants.Select(p => SendCommitRequestAsync(p, cancellationToken)).ToList();

        // Wait for all responses
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        try
        {
            await Task.WhenAll(commitTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _logger?.LogWarning("Commit phase timed out for transaction {TransactionId}", _transactionId);
            return CommitResult.Timeout;
        }

        // Check if all participants committed
        lock (_stateLock)
        {
            var allCommitted = _participantStates.Values.All(s => s.Status == ParticipantStatus.Committed);
            var anyFailed = _participantStates.Values.Any(s => s.Status == ParticipantStatus.Failed);

            if (allCommitted)
            {
                _logger?.LogInformation("All participants committed transaction {TransactionId}", _transactionId);
                return CommitResult.Success;
            }
            else if (anyFailed)
            {
                _logger?.LogWarning("Some participants failed to commit transaction {TransactionId}", _transactionId);
                return CommitResult.Failed;
            }
            else
            {
                _logger?.LogWarning("Commit phase incomplete for transaction {TransactionId}", _transactionId);
                return CommitResult.Incomplete;
            }
        }
    }

    /// <summary>
    /// Executes the abort phase of 2PC.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ExecuteAbortPhaseAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Starting abort phase for transaction {TransactionId}", _transactionId);

        // Send abort requests to all participants
        var abortTasks = _participants.Select(p => SendAbortRequestAsync(p, cancellationToken)).ToList();

        // Wait for all responses (with timeout, but don't fail if some don't respond)
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        try
        {
            await Task.WhenAll(abortTasks).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Some participants failed to abort transaction {TransactionId}", _transactionId);
        }

        _logger?.LogInformation("Abort phase completed for transaction {TransactionId}", _transactionId);
    }

    /// <summary>
    /// Processes a protocol message.
    /// </summary>
    /// <param name="message">The message to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ProcessMessageAsync(ProtocolMessage message, CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            if (!_participantStates.TryGetValue(message.ParticipantId, out var state))
            {
                _logger?.LogWarning("Received message from unknown participant {ParticipantId}", message.ParticipantId);
                return;
            }

            switch (message.MessageType)
            {
                case MessageType.PrepareResponse:
                    state.Vote = message.Vote ?? Vote.Abort;
                    break;

                case MessageType.CommitResponse:
                    state.Status = message.CommitStatus ?? ParticipantStatus.Failed;
                    break;

                case MessageType.AbortResponse:
                    state.Status = ParticipantStatus.Aborted;
                    break;
            }
        }
    }

    /// <summary>
    /// Sends a prepare request to a participant.
    /// </summary>
    /// <param name="participant">The participant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task SendPrepareRequestAsync(ITransactionParticipant participant, CancellationToken cancellationToken)
    {
        try
        {
            var vote = await participant.PrepareAsync(_transactionId, cancellationToken).ConfigureAwait(false);

            lock (_stateLock)
            {
                _participantStates[participant.ParticipantId].Vote = vote;
            }

            _logger?.LogDebug("Participant {ParticipantId} voted {Vote} for transaction {TransactionId}",
                participant.ParticipantId, vote, _transactionId);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Participant {ParticipantId} failed to prepare for transaction {TransactionId}",
                participant.ParticipantId, _transactionId);

            lock (_stateLock)
            {
                _participantStates[participant.ParticipantId].Vote = Vote.Abort;
            }
        }
    }

    /// <summary>
    /// Sends a commit request to a participant.
    /// </summary>
    /// <param name="participant">The participant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task SendCommitRequestAsync(ITransactionParticipant participant, CancellationToken cancellationToken)
    {
        try
        {
            await participant.CommitAsync(_transactionId, cancellationToken).ConfigureAwait(false);

            lock (_stateLock)
            {
                _participantStates[participant.ParticipantId].Status = ParticipantStatus.Committed;
            }

            _logger?.LogDebug("Participant {ParticipantId} committed transaction {TransactionId}",
                participant.ParticipantId, _transactionId);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Participant {ParticipantId} failed to commit transaction {TransactionId}",
                participant.ParticipantId, _transactionId);

            lock (_stateLock)
            {
                _participantStates[participant.ParticipantId].Status = ParticipantStatus.Failed;
            }
        }
    }

    /// <summary>
    /// Sends an abort request to a participant.
    /// </summary>
    /// <param name="participant">The participant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task SendAbortRequestAsync(ITransactionParticipant participant, CancellationToken cancellationToken)
    {
        try
        {
            await participant.AbortAsync(_transactionId, cancellationToken).ConfigureAwait(false);

            lock (_stateLock)
            {
                _participantStates[participant.ParticipantId].Status = ParticipantStatus.Aborted;
            }

            _logger?.LogDebug("Participant {ParticipantId} aborted transaction {TransactionId}",
                participant.ParticipantId, _transactionId);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Participant {ParticipantId} failed to abort transaction {TransactionId}",
                participant.ParticipantId, _transactionId);
        }
    }
}

/// <summary>
/// Represents the state of a transaction participant.
/// </summary>
internal class ParticipantState
{
    /// <summary>Gets the participant identifier.</summary>
    public string ParticipantId { get; }

    /// <summary>Gets or sets the participant's vote.</summary>
    public Vote Vote { get; set; }

    /// <summary>Gets or sets the participant's status.</summary>
    public ParticipantStatus Status { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ParticipantState"/> class.
    /// </summary>
    /// <param name="participantId">The participant identifier.</param>
    public ParticipantState(string participantId)
    {
        ParticipantId = participantId;
        Vote = Vote.Unknown;
        Status = ParticipantStatus.Unknown;
    }
}

/// <summary>
/// Prepare phase results.
/// </summary>
public enum PrepareResult
{
    /// <summary>Prepare phase succeeded.</summary>
    Success,

    /// <summary>Prepare phase failed.</summary>
    Abort,

    /// <summary>Prepare phase timed out.</summary>
    Timeout,

    /// <summary>Prepare phase incomplete.</summary>
    Incomplete
}

/// <summary>
/// Commit phase results.
/// </summary>
public enum CommitResult
{
    /// <summary>Commit succeeded.</summary>
    Success,

    /// <summary>Commit failed.</summary>
    Failed,

    /// <summary>Commit timed out.</summary>
    Timeout,

    /// <summary>Commit incomplete.</summary>
    Incomplete,

    /// <summary>Transaction was aborted.</summary>
    Aborted
}

/// <summary>
/// Participant votes.
/// </summary>
public enum Vote
{
    /// <summary>Unknown vote.</summary>
    Unknown,

    /// <summary>Vote to commit.</summary>
    Prepared,

    /// <summary>Vote to abort.</summary>
    Abort
}

/// <summary>
/// Participant status.
/// </summary>
public enum ParticipantStatus
{
    /// <summary>Unknown status.</summary>
    Unknown,

    /// <summary>Participant is prepared.</summary>
    Prepared,

    /// <summary>Participant committed.</summary>
    Committed,

    /// <summary>Participant aborted.</summary>
    Aborted,

    /// <summary>Participant failed.</summary>
    Failed
}

/// <summary>
/// Message types.
/// </summary>
public enum MessageType
{
    /// <summary>Prepare request.</summary>
    PrepareRequest,

    /// <summary>Prepare response.</summary>
    PrepareResponse,

    /// <summary>Commit request.</summary>
    CommitRequest,

    /// <summary>Commit response.</summary>
    CommitResponse,

    /// <summary>Abort request.</summary>
    AbortRequest,

    /// <summary>Abort response.</summary>
    AbortResponse
}

/// <summary>
/// Protocol message.
/// </summary>
public class ProtocolMessage
{
    /// <summary>Gets the message type.</summary>
    public MessageType MessageType { get; }

    /// <summary>Gets the transaction identifier.</summary>
    public string TransactionId { get; }

    /// <summary>Gets the participant identifier.</summary>
    public string ParticipantId { get; }

    /// <summary>Gets the vote (for prepare responses).</summary>
    public Vote? Vote { get; }

    /// <summary>Gets the commit status (for commit responses).</summary>
    public ParticipantStatus? CommitStatus { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProtocolMessage"/> class.
    /// </summary>
    /// <param name="messageType">The message type.</param>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="participantId">The participant identifier.</param>
    /// <param name="vote">The vote.</param>
    /// <param name="commitStatus">The commit status.</param>
    public ProtocolMessage(
        MessageType messageType,
        string transactionId,
        string participantId,
        Vote? vote = null,
        ParticipantStatus? commitStatus = null)
    {
        MessageType = messageType;
        TransactionId = transactionId;
        ParticipantId = participantId;
        Vote = vote;
        CommitStatus = commitStatus;
    }
}
