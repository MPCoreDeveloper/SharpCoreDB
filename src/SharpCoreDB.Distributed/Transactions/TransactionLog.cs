// <copyright file="TransactionLog.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Distributed.Transactions;

/// <summary>
/// Persistent log for distributed transaction events and recovery.
/// Provides audit trail and recovery information for distributed transactions.
/// C# 14: Async streams, collection expressions, modern I/O.
/// </summary>
public sealed class TransactionLog : IAsyncDisposable
{
    private readonly string _logPath;
    private readonly ILogger<TransactionLog>? _logger;

    private readonly Stream _logStream;
    private readonly StreamWriter _logWriter;
    private readonly Lock _logLock = new();

    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionLog"/> class.
    /// </summary>
    /// <param name="logPath">The path to the transaction log file.</param>
    /// <param name="logger">Optional logger.</param>
    public TransactionLog(string logPath, ILogger<TransactionLog>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);

        _logPath = logPath;
        _logger = logger;

        // Open log file for append
        _logStream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        _logWriter = new StreamWriter(_logStream, leaveOpen: true) { AutoFlush = true };

        _logger?.LogInformation("Transaction log initialized at {LogPath}", logPath);
    }

    /// <summary>
    /// Logs a transaction begin event.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="participantIds">The participant identifiers.</param>
    /// <param name="isolationLevel">The isolation level.</param>
    /// <param name="timeout">The timeout.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task LogTransactionBeginAsync(
        string transactionId,
        IReadOnlyList<string> participantIds,
        IsolationLevel isolationLevel,
        TimeSpan timeout)
    {
        var entry = new TransactionLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TransactionId = transactionId,
            EventType = TransactionEventType.Begin,
            ParticipantIds = [.. participantIds],
            IsolationLevel = isolationLevel,
            Timeout = timeout
        };

        await WriteLogEntryAsync(entry).ConfigureAwait(false);
    }

    /// <summary>
    /// Logs a prepare event.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="participantId">The participant identifier.</param>
    /// <param name="vote">The participant's vote.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task LogPrepareAsync(string transactionId, string participantId, Vote vote)
    {
        var entry = new TransactionLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TransactionId = transactionId,
            EventType = TransactionEventType.Prepare,
            ParticipantId = participantId,
            Vote = vote
        };

        await WriteLogEntryAsync(entry).ConfigureAwait(false);
    }

    /// <summary>
    /// Logs a commit event.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="participantId">The participant identifier.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task LogCommitAsync(string transactionId, string participantId)
    {
        var entry = new TransactionLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TransactionId = transactionId,
            EventType = TransactionEventType.Commit,
            ParticipantId = participantId
        };

        await WriteLogEntryAsync(entry).ConfigureAwait(false);
    }

    /// <summary>
    /// Logs an abort event.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="participantId">The participant identifier.</param>
    /// <param name="reason">The abort reason.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task LogAbortAsync(string transactionId, string participantId, string? reason = null)
    {
        var entry = new TransactionLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TransactionId = transactionId,
            EventType = TransactionEventType.Abort,
            ParticipantId = participantId,
            Reason = reason
        };

        await WriteLogEntryAsync(entry).ConfigureAwait(false);
    }

    /// <summary>
    /// Logs a transaction completion event.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="status">The final transaction status.</param>
    /// <param name="duration">The transaction duration.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task LogTransactionCompleteAsync(string transactionId, TransactionStatus status, TimeSpan duration)
    {
        var entry = new TransactionLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TransactionId = transactionId,
            EventType = TransactionEventType.Complete,
            FinalStatus = status,
            Duration = duration
        };

        await WriteLogEntryAsync(entry).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads transaction log entries for recovery.
    /// </summary>
    /// <param name="since">Only return entries since this timestamp.</param>
    /// <returns>An async enumerable of log entries.</returns>
    public async IAsyncEnumerable<TransactionLogEntry> ReadLogEntriesAsync(DateTimeOffset? since = null)
    {
        // Open log file for reading
        await using var fileStream = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fileStream);

        await foreach (var line in ReadLinesAsync(reader).ConfigureAwait(false))
        {
            TransactionLogEntry? entry = null;
            try
            {
                entry = JsonSerializer.Deserialize<TransactionLogEntry>(line);
            }
            catch (JsonException ex)
            {
                _logger?.LogWarning(ex, "Failed to deserialize log entry: {Line}", line);
            }

            if (entry is not null && (since is null || entry.Timestamp >= since))
            {
                yield return entry;
            }
        }
    }

    /// <summary>
    /// Reads lines asynchronously from a stream reader.
    /// </summary>
    /// <param name="reader">The stream reader.</param>
    /// <returns>An async enumerable of lines.</returns>
    private static async IAsyncEnumerable<string> ReadLinesAsync(StreamReader reader)
    {
        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            yield return line;
        }
    }

    /// <summary>
    /// Gets recovery information for incomplete transactions.
    /// </summary>
    /// <returns>A list of transactions that need recovery.</returns>
    public async Task<IReadOnlyList<RecoveryInfo>> GetRecoveryInfoAsync()
    {
        var transactionStates = new Dictionary<string, TransactionRecoveryState>();

        await foreach (var entry in ReadLogEntriesAsync().ConfigureAwait(false))
        {
            if (!transactionStates.TryGetValue(entry.TransactionId, out var state))
            {
                state = new TransactionRecoveryState(entry.TransactionId);
                transactionStates[entry.TransactionId] = state;
            }

            state.ProcessLogEntry(entry);
        }

        return transactionStates.Values
            .Where(s => s.NeedsRecovery)
            .Select(s => new RecoveryInfo
            {
                TransactionId = s.TransactionId,
                Status = s.Status,
                Participants = [.. s.Participants],
                LastEvent = s.LastEvent
            })
            .ToList();
    }

    /// <summary>
    /// Truncates old log entries.
    /// </summary>
    /// <param name="retentionPeriod">How long to retain log entries.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task TruncateLogAsync(TimeSpan retentionPeriod)
    {
        var cutoff = DateTimeOffset.UtcNow - retentionPeriod;
        var tempPath = _logPath + ".tmp";

        try
        {
            await using var tempStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await using var tempWriter = new StreamWriter(tempStream, leaveOpen: false);

            await foreach (var entry in ReadLogEntriesAsync().ConfigureAwait(false))
            {
                if (entry.Timestamp >= cutoff)
                {
                    var json = JsonSerializer.Serialize(entry);
                    await tempWriter.WriteLineAsync(json).ConfigureAwait(false);
                }
            }

            await tempWriter.FlushAsync().ConfigureAwait(false);
            tempStream.Close();

            // Atomic replace
            File.Move(tempPath, _logPath, overwrite: true);

            _logger?.LogInformation("Transaction log truncated, retaining entries newer than {Cutoff}", cutoff);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to truncate transaction log");

            // Clean up temp file
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }

            throw;
        }
    }

    /// <summary>
    /// Writes a log entry to the file.
    /// </summary>
    /// <param name="entry">The log entry to write.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task WriteLogEntryAsync(TransactionLogEntry entry)
    {
        var json = JsonSerializer.Serialize(entry);

        lock (_logLock)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TransactionLog));
            }

            _logWriter.WriteLine(json);
        }

        _logger?.LogTrace("Logged transaction event: {EventType} for {TransactionId}", entry.EventType, entry.TransactionId);
    }

    /// <summary>
    /// Disposes the transaction log.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await _logWriter.DisposeAsync().ConfigureAwait(false);
        await _logStream.DisposeAsync().ConfigureAwait(false);

        _logger?.LogInformation("Transaction log disposed");
    }
}

/// <summary>
/// Transaction log entry.
/// </summary>
public class TransactionLogEntry
{
    /// <summary>Gets or sets the timestamp.</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Gets or sets the transaction identifier.</summary>
    public required string TransactionId { get; set; }

    /// <summary>Gets or sets the event type.</summary>
    public TransactionEventType EventType { get; set; }

    /// <summary>Gets or sets the participant identifier.</summary>
    public string? ParticipantId { get; set; }

    /// <summary>Gets or sets the participant identifiers.</summary>
    public IReadOnlyList<string>? ParticipantIds { get; set; }

    /// <summary>Gets or sets the vote.</summary>
    public Vote? Vote { get; set; }

    /// <summary>Gets or sets the isolation level.</summary>
    public IsolationLevel? IsolationLevel { get; set; }

    /// <summary>Gets or sets the timeout.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Gets or sets the final status.</summary>
    public TransactionStatus? FinalStatus { get; set; }

    /// <summary>Gets or sets the duration.</summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>Gets or sets the reason.</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Transaction event types.
/// </summary>
public enum TransactionEventType
{
    /// <summary>Transaction begin.</summary>
    Begin,

    /// <summary>Prepare phase.</summary>
    Prepare,

    /// <summary>Commit phase.</summary>
    Commit,

    /// <summary>Abort phase.</summary>
    Abort,

    /// <summary>Transaction complete.</summary>
    Complete
}

/// <summary>
/// Recovery information for incomplete transactions.
/// </summary>
public class RecoveryInfo
{
    /// <summary>Gets the transaction identifier.</summary>
    public required string TransactionId { get; init; }

    /// <summary>Gets the transaction status.</summary>
    public TransactionStatus Status { get; init; }

    /// <summary>Gets the participant identifiers.</summary>
    public required IReadOnlyList<string> Participants { get; init; }

    /// <summary>Gets the last logged event.</summary>
    public TransactionLogEntry? LastEvent { get; init; }
}

/// <summary>
/// Internal state for transaction recovery.
/// </summary>
internal class TransactionRecoveryState
{
    private readonly List<string> _participants = [];
    private TransactionLogEntry? _lastEvent;

    /// <summary>Gets the transaction identifier.</summary>
    public string TransactionId { get; }

    /// <summary>Gets the participants.</summary>
    public IReadOnlyList<string> Participants => [.. _participants];

    /// <summary>Gets the last event.</summary>
    public TransactionLogEntry? LastEvent => _lastEvent;

    /// <summary>Gets the transaction status.</summary>
    public TransactionStatus Status { get; private set; }

    /// <summary>Gets whether the transaction needs recovery.</summary>
    public bool NeedsRecovery => Status is TransactionStatus.Preparing or TransactionStatus.Committing or TransactionStatus.Aborting;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionRecoveryState"/> class.
    /// </summary>
    /// <param name="transactionId">The transaction identifier.</param>
    public TransactionRecoveryState(string transactionId)
    {
        TransactionId = transactionId;
        Status = TransactionStatus.Active;
    }

    /// <summary>
    /// Processes a log entry.
    /// </summary>
    /// <param name="entry">The log entry.</param>
    public void ProcessLogEntry(TransactionLogEntry entry)
    {
        _lastEvent = entry;

        switch (entry.EventType)
        {
            case TransactionEventType.Begin:
                if (entry.ParticipantIds is not null)
                {
                    _participants.AddRange(entry.ParticipantIds);
                }
                Status = TransactionStatus.Active;
                break;

            case TransactionEventType.Prepare:
                Status = TransactionStatus.Preparing;
                break;

            case TransactionEventType.Commit:
                Status = TransactionStatus.Committing;
                break;

            case TransactionEventType.Abort:
                Status = TransactionStatus.Aborting;
                break;

            case TransactionEventType.Complete:
                Status = entry.FinalStatus ?? TransactionStatus.Unknown;
                break;
        }
    }
}
