// <copyright file="SharpCoreDbOutboxStoreRetryTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.CQRS.Tests;

using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;

/// <summary>
/// The retry state machine of <see cref="SharpCoreDbOutboxStore"/>: backoff gating, the exponential schedule,
/// the dead-letter transition, the legacy 5-column schema, SQL-literal escaping and the argument/validation
/// contract.
/// Added with the mutation baseline — 77 mutants survived the existing suite, which covered the happy path
/// (add, get, mark, requeue, duplicate) but never asserted the scheduling column, the backoff arithmetic or
/// the dead-letter edge. Those are the parts that decide whether a failing message is retried, delayed or
/// dropped.
/// </summary>
public sealed class SharpCoreDbOutboxStoreRetryTests : IAsyncDisposable
{
    private readonly List<IDatabase> _dbs = [];
    private readonly List<string> _paths = [];
    private readonly DatabaseFactory _factory;

    public SharpCoreDbOutboxStoreRetryTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
    }

    [Fact]
    public async Task GetUnpublishedAsync_AfterAFailure_HidesTheMessageUntilItsBackoffElapses()
    {
        // Arrange — a one-minute backoff means the retry is not due yet
        var (_, store) = CreateStore(new OutboxRetryPolicyOptions
        {
            MaxAttempts = 5,
            BaseDelay = TimeSpan.FromMinutes(1),
            MaxDelay = TimeSpan.FromMinutes(10),
            DeadLetterTableName = "scdb_outbox_deadletter",
        });
        await store.AddAsync(MakeMessage("backoff-1"));

        // Act
        await store.RecordFailureAsync("backoff-1", "transient");
        var unpublished = await store.GetUnpublishedAsync(10);

        // Assert — the message is still stored, but not due
        Assert.Empty(unpublished);
    }

    [Fact]
    public async Task RecordFailureAsync_SchedulesExponentialBackoffCappedAtMaxDelay()
    {
        // Arrange — 2s base, 5s cap: attempts 1..3 must schedule +2s, +4s, +5s
        var (db, store) = CreateStore(new OutboxRetryPolicyOptions
        {
            MaxAttempts = 10,
            BaseDelay = TimeSpan.FromSeconds(2),
            MaxDelay = TimeSpan.FromSeconds(5),
            DeadLetterTableName = "scdb_outbox_deadletter",
        });
        await store.AddAsync(MakeMessage("schedule-1"));

        // Act / Assert — each failure reads back the delay it wrote
        foreach (var (attempt, lowMs, highMs) in new[] { (1, 1500.0, 3000.0), (2, 3000.0, 5000.0), (3, 4000.0, 6000.0) })
        {
            var before = DateTimeOffset.UtcNow;
            await store.RecordFailureAsync("schedule-1", "transient");
            var next = ReadNextAttemptUtc(db, "schedule-1");

            Assert.NotNull(next);
            Assert.Equal(attempt, (int)ReadAttemptCount(db, "schedule-1"));

            var delayMs = (next!.Value - before).TotalMilliseconds;
            Assert.True(delayMs >= lowMs && delayMs <= highMs,
                $"attempt {attempt}: expected a delay in [{lowMs}, {highMs}] ms, measured {delayMs:N0} ms.");
        }
    }

    [Fact]
    public async Task RecordFailureAsync_WhenAttemptsAreExhausted_MovesTheMessageToTheDeadLetterTable()
    {
        // Arrange
        var (db, store) = CreateStore(MaxAttemptsPolicy(maxAttempts: 2));
        await store.AddAsync(MakeMessage("dead-1"));

        // Act — the second failure reaches MaxAttempts
        await store.RecordFailureAsync("dead-1", "boom");
        await store.RecordFailureAsync("dead-1", "boom again");

        // Assert — gone from the outbox, present in the dead-letter table with the failure reason
        Assert.Empty(db.ExecuteQuery("SELECT message_id FROM scdb_outbox"));
        var dead = db.ExecuteQuery("SELECT message_id, attempt_count, last_error FROM scdb_outbox_deadletter");
        Assert.Single(dead);
        Assert.Equal("dead-1", dead[0]["message_id"]?.ToString());
        Assert.Equal("2", dead[0]["attempt_count"]?.ToString());
        Assert.Equal("boom again", dead[0]["last_error"]?.ToString());
        Assert.Empty(await store.GetUnpublishedAsync(10));
    }

    [Fact]
    public async Task RecordFailureAsync_BelowTheLimit_KeepsTheMessageInTheOutbox()
    {
        // Arrange — MaxAttempts 3, one failure: the message must stay in the outbox
        var (db, store) = CreateStore(MaxAttemptsPolicy(maxAttempts: 3));
        await store.AddAsync(MakeMessage("retry-1"));

        // Act
        await store.RecordFailureAsync("retry-1", "transient");

        // Assert — the message stays in the outbox and no dead-letter table was created
        Assert.Single(db.ExecuteQuery("SELECT message_id FROM scdb_outbox"));
        Assert.False(db.TryGetTable("scdb_outbox_deadletter", out _));
    }

    [Fact]
    public async Task GetDeadLettersAsync_RespectsLimitAndNegativeLimitReturnsEmpty()
    {
        // Arrange — two dead-lettered messages
        var (_, store) = CreateStore(MaxAttemptsPolicy(maxAttempts: 1));
        await store.AddAsync(MakeMessage("dl-a"));
        await store.AddAsync(MakeMessage("dl-b"));
        await store.RecordFailureAsync("dl-a", "boom");
        await store.RecordFailureAsync("dl-b", "boom");

        // Act / Assert
        Assert.Single(await store.GetDeadLettersAsync(1));
        Assert.Equal(2, (await store.GetDeadLettersAsync(10)).Count);
        Assert.Empty(await store.GetDeadLettersAsync(-1));
    }

    [Fact]
    public async Task GetDeadLettersAsync_WithNoDeadLetterTable_ReturnsEmpty()
    {
        var (_, store) = CreateStore(MaxAttemptsPolicy(maxAttempts: 5));

        Assert.Empty(await store.GetDeadLettersAsync(10));
    }

    [Fact]
    public async Task LegacyFiveColumnTable_StillAddsAndReturnsMessages()
    {
        // Arrange — a pre-existing outbox from before the retry columns existed
        var db = CreateDatabase();
        db.ExecuteSQL("CREATE TABLE legacy_outbox (message_id TEXT, aggregate_id TEXT, message_type TEXT, payload_hex TEXT, created_at_utc TEXT)");
        var store = new SharpCoreDbOutboxStore(db, "legacy_outbox");

        // Act
        var added = await store.AddAsync(MakeMessage("legacy-1"));
        var unpublished = await store.GetUnpublishedAsync(10);

        // Assert — the 5-column INSERT path and the no-scheduling query path both work
        Assert.True(added);
        Assert.Single(unpublished);
        Assert.Equal("legacy-1", unpublished[0].MessageId);
    }

    [Fact]
    public async Task AddAsync_RoundTripsThePayloadBytes()
    {
        // Arrange
        var (_, store) = CreateStore();
        await store.AddAsync(MakeMessage("payload-1", payload: [1, 2, 3, 255]));
        await store.AddAsync(MakeMessage("payload-empty", payload: []));

        // Act
        var unpublished = await store.GetUnpublishedAsync(10);

        // Assert — hex encoding survives the round-trip in both directions
        var withPayload = unpublished.Single(m => m.MessageId == "payload-1");
        Assert.Equal([1, 2, 3, 255], withPayload.Payload.ToArray());

        var empty = unpublished.Single(m => m.MessageId == "payload-empty");
        Assert.Empty(empty.Payload.ToArray());
    }

    [Fact]
    public async Task AddAsync_WithApostrophesInIdentifiers_RoundTrips()
    {
        // Arrange — the values are interpolated into SQL, so escaping must survive
        var (_, store) = CreateStore();
        const string trickyId = "o'1";
        await store.AddAsync(MakeMessage(trickyId, type: "Type'A"));

        // Act
        var unpublished = await store.GetUnpublishedAsync(10);

        // Assert
        Assert.Single(unpublished);
        Assert.Equal(trickyId, unpublished[0].MessageId);
        Assert.Equal("Type'A", unpublished[0].MessageType);
    }

    [Fact]
    public void AddAsync_WithBlankMessageId_ThrowsArgumentException()
    {
        var (_, store) = CreateStore();

        Assert.Throws<ArgumentException>(() => { _ = store.AddAsync(MakeMessage(" ")); });
    }

    [Fact]
    public void AddAsync_WithBlankMessageType_ThrowsArgumentException()
    {
        var (_, store) = CreateStore();

        Assert.Throws<ArgumentException>(() => { _ = store.AddAsync(MakeMessage("m", type: "  ")); });
    }

    [Fact]
    public void AddAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        var (_, store) = CreateStore();

        Assert.Throws<OperationCanceledException>(() =>
        {
            _ = store.AddAsync(MakeMessage("m"), new CancellationToken(canceled: true));
        });
    }


    [Fact]
    public void MarkPublishedAsync_WithBlankMessageId_ThrowsArgumentException()
    {
        var (_, store) = CreateStore();

        Assert.Throws<ArgumentException>(() => { _ = store.MarkPublishedAsync(" "); });
    }

    [Fact]
    public void RecordFailureAsync_WithBlankError_ThrowsArgumentException()
    {
        var (_, store) = CreateStore();

        Assert.Throws<ArgumentException>(() => { _ = store.RecordFailureAsync("m", " "); });
    }

    [Fact]
    public void RequeueDeadLetterAsync_WithBlankMessageId_ThrowsArgumentException()
    {
        var (_, store) = CreateStore();

        Assert.Throws<ArgumentException>(() => { _ = store.RequeueDeadLetterAsync("  "); });
    }

    [Fact]
    public void Constructor_WithBlankTableName_ThrowsArgumentException()
    {
        var db = CreateDatabase();

        Assert.Throws<ArgumentException>(() => new SharpCoreDbOutboxStore(db, "  "));
    }

    [Fact]
    public void Constructor_WithIllegalTableNameCharacters_ThrowsArgumentException()
    {
        var db = CreateDatabase();

        Assert.Throws<ArgumentException>(() => new SharpCoreDbOutboxStore(db, "outbox; DROP"));
    }

    [Fact]
    public void Constructor_WithBlankDeadLetterTableName_ThrowsArgumentException()
    {
        var db = CreateDatabase();

        Assert.Throws<ArgumentException>(() => new SharpCoreDbOutboxStore(db, retryPolicy: new OutboxRetryPolicyOptions
        {
            MaxAttempts = 3,
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(5),
            DeadLetterTableName = " ",
        }));
    }

    [Fact]
    public void Constructor_WithZeroBaseDelay_ThrowsArgumentException()
    {
        var db = CreateDatabase();

        Assert.Throws<ArgumentException>(() => new SharpCoreDbOutboxStore(db, retryPolicy: new OutboxRetryPolicyOptions
        {
            MaxAttempts = 3,
            BaseDelay = TimeSpan.Zero,
            MaxDelay = TimeSpan.FromSeconds(5),
            DeadLetterTableName = "scdb_outbox_deadletter",
        }));
    }

    [Fact]
    public void Constructor_WithZeroMaxAttempts_ThrowsArgumentException()
    {
        var db = CreateDatabase();

        Assert.Throws<ArgumentException>(() => new SharpCoreDbOutboxStore(db, retryPolicy: new OutboxRetryPolicyOptions
        {
            MaxAttempts = 0,
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(5),
            DeadLetterTableName = "scdb_outbox_deadletter",
        }));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var db in _dbs)
        {
            await db.DisposeAsync();
        }

        foreach (var path in _paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // Ignore cleanup errors.
            }
        }
    }

    private static OutboxRetryPolicyOptions MaxAttemptsPolicy(int maxAttempts) => new()
    {
        MaxAttempts = maxAttempts,
        BaseDelay = TimeSpan.FromSeconds(2),
        MaxDelay = TimeSpan.FromSeconds(5),
        DeadLetterTableName = "scdb_outbox_deadletter",
    };

    private (IDatabase Db, SharpCoreDbOutboxStore Store) CreateStore(
        OutboxRetryPolicyOptions? policy = null,
        string tableName = "scdb_outbox")
    {
        var db = CreateDatabase();
        return (db, new SharpCoreDbOutboxStore(db, tableName, policy));
    }

    private IDatabase CreateDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SharpCoreDB_OutboxRetry_{Guid.NewGuid():N}");
        _paths.Add(path);
        var db = _factory.Create(path, "outbox-test-password");
        _dbs.Add(db);
        return db;
    }

    private static OutboxMessage MakeMessage(string id, string type = "TypeA", byte[]? payload = null) =>
        new(id, "agg-1", type, payload ?? "{ }"u8.ToArray(), DateTimeOffset.UtcNow, false);

    private static long ReadAttemptCount(IDatabase db, string messageId)
    {
        var rows = db.ExecuteQuery($"SELECT attempt_count FROM scdb_outbox WHERE message_id = '{messageId}'");
        return rows.Count == 0 ? -1 : Convert.ToInt64(rows[0]["attempt_count"], CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ReadNextAttemptUtc(IDatabase db, string messageId)
    {
        var rows = db.ExecuteQuery($"SELECT next_attempt_utc FROM scdb_outbox WHERE message_id = '{messageId}'");
        if (rows.Count == 0)
        {
            return null;
        }

        var raw = rows[0]["next_attempt_utc"]?.ToString();
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}

