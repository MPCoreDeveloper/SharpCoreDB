// <copyright file="ITransactionParticipant.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Distributed.Transactions;

/// <summary>
/// Interface for transaction participants in distributed transactions.
/// Participants must implement prepare, commit, and abort operations.
/// </summary>
public interface ITransactionParticipant
{
    /// <summary>Gets the unique participant identifier.</summary>
    string ParticipantId { get; }

    /// <summary>
    /// Prepares the participant for transaction commit.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The participant's vote (Prepared or Abort).</returns>
    Task<Vote> PrepareAsync(string transactionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the participant's changes.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task CommitAsync(string transactionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aborts the participant's changes.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task AbortAsync(string transactionId, CancellationToken cancellationToken = default);
}
