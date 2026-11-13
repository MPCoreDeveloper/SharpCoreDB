// <copyright file="OutboxDispatchObservabilityTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.CQRS.Tests;

using Microsoft.Extensions.Logging;

/// <summary>
/// The dispatch contract of <see cref="OutboxDispatchService"/> and
/// <see cref="OutboxDispatchBackgroundService"/>: which messages are published and marked, what happens to
/// a failing one, what a cancellation must NOT be recorded as, the summary the operators read, and the
/// options the hosted loop refuses to start with.
/// Added with the mutation baseline — both classes were almost entirely unpinned (17 + 21 survivors, and
/// the three validation throws had no coverage at all), so a dispatch that marked a message published
/// without publishing it, or recorded a cancellation as a message failure, would have stayed green.
/// </summary>
public sealed class OutboxDispatchObservabilityTests
{
    [Fact]
    public async Task DispatchUnpublishedAsync_PublishesAndMarksEveryMessage()
    {
        // Arrange
        var store = new FakeOutboxStore();
        store.Unpublished.Add(MakeMessage("a"));
        store.Unpublished.Add(MakeMessage("b"));
        var publisher = new FakePublisher();
        var sut = new OutboxDispatchService(store, publisher);

        // Act
        var published = await sut.DispatchUnpublishedAsync(10);

        // Assert — both published, both marked, count is what was dispatched
        Assert.Equal(2, published);
        Assert.Equal(["a", "b"], publisher.Published);
        Assert.Equal(["a", "b"], store.MarkedPublished);
        Assert.Empty(store.RecordedFailures);
    }

    [Fact]
    public async Task DispatchUnpublishedAsync_WithZeroLimit_ReturnsZeroWithoutQueryingTheStore()
    {
        var store = new FakeOutboxStore();
        var sut = new OutboxDispatchService(store, new FakePublisher());

        var published = await sut.DispatchUnpublishedAsync(0);

        Assert.Equal(0, published);
        Assert.Equal(0, store.GetUnpublishedCalls);
    }

    [Fact]
    public async Task DispatchUnpublishedAsync_WithNegativeLimit_ReturnsZero()
    {
        var store = new FakeOutboxStore();
        var sut = new OutboxDispatchService(store, new FakePublisher());

        var published = await sut.DispatchUnpublishedAsync(-5);

        Assert.Equal(0, published);
        Assert.Equal(0, store.GetUnpublishedCalls);
    }

    [Fact]
    public async Task DispatchUnpublishedAsync_WhenPublishFails_RecordsTheFailureAndContinues()
    {
        // Arrange — the first message throws, the second must still be dispatched
        var store = new FakeOutboxStore();
        store.Unpublished.Add(MakeMessage("bad"));
        store.Unpublished.Add(MakeMessage("good"));
        var publisher = new FakePublisher();
        publisher.FailFor.Add("bad");
        var sut = new OutboxDispatchService(store, publisher);

        // Act
        var published = await sut.DispatchUnpublishedAsync(10);

        // Assert — one recorded failure with the exception message, one published, bad one not marked
        Assert.Equal(1, published);
        var failure = Assert.Single(store.RecordedFailures);
        Assert.Equal("bad", failure.Id);
        Assert.Equal("publish failed for bad", failure.Error);
        Assert.Equal(["good"], store.MarkedPublished);
        Assert.Equal(["good"], publisher.Published);
    }

    [Fact]
    public async Task DispatchUnpublishedAsync_WhenPublishIsCancelled_PropagatesAndRecordsNoFailure()
    {
        // Arrange — a cancellation is not a message failure and must not be recorded as one
        var store = new FakeOutboxStore();
        store.Unpublished.Add(MakeMessage("a"));
        var publisher = new FakePublisher();
        publisher.CancelFor.Add("a");
        var sut = new OutboxDispatchService(store, publisher);

        // Act / Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.DispatchUnpublishedAsync(10));
        Assert.Empty(store.RecordedFailures);
        Assert.Empty(store.MarkedPublished);
    }

    [Fact]
    public async Task DispatchUnpublishedAsync_LogsTheSummaryWithTheCounts()
    {
        // Arrange — one failure and one success, three dead letters on record
        var store = new FakeOutboxStore();
        store.Unpublished.Add(MakeMessage("bad"));
        store.Unpublished.Add(MakeMessage("good"));
        store.DeadLetters.Add(MakeMessage("dead-1"));
        store.DeadLetters.Add(MakeMessage("dead-2"));
        store.DeadLetters.Add(MakeMessage("dead-3"));
        var publisher = new FakePublisher();
        publisher.FailFor.Add("bad");
        var logger = new RecordingLogger<OutboxDispatchService>();
        var sut = new OutboxDispatchService(store, publisher, logger);

        // Act
        await sut.DispatchUnpublishedAsync(10);

        // Assert — the operators' summary carries the real numbers
        var entry = Assert.Single(logger.Entries, e => e.Message.Contains("Outbox dispatch completed", StringComparison.Ordinal));
        Assert.Contains("Published: 1", entry.Message, StringComparison.Ordinal);
        Assert.Contains("Failed: 1", entry.Message, StringComparison.Ordinal);
        Assert.Contains("Total: 2", entry.Message, StringComparison.Ordinal);
        Assert.Contains("DeadLetters: 3", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DispatchUnpublishedAsync_WithNothingToDo_LogsNoSummary()
    {
        // Arrange — no messages, so the summary branch must not run
        var store = new FakeOutboxStore();
        var logger = new RecordingLogger<OutboxDispatchService>();
        var sut = new OutboxDispatchService(store, new FakePublisher(), logger);

        // Act
        var published = await sut.DispatchUnpublishedAsync(10);

        // Assert
        Assert.Equal(0, published);
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("Outbox dispatch completed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DispatchUnpublishedAsync_WhenCancelledBeforeStart_Throws()
    {
        var store = new FakeOutboxStore();
        var sut = new OutboxDispatchService(store, new FakePublisher());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.DispatchUnpublishedAsync(10, new CancellationToken(canceled: true)));

        Assert.Equal(0, store.GetUnpublishedCalls);
    }

    [Fact]
    public void Constructor_WithNullStore_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new OutboxDispatchService(null!, new FakePublisher()));
    }

    [Fact]
    public void Constructor_WithNullPublisher_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new OutboxDispatchService(new FakeOutboxStore(), null!));
    }


    // ---- background loop ----

    [Fact]
    public async Task BackgroundService_WithZeroBatchSize_RefusesToStart()
    {
        var sut = CreateBackgroundService(new OutboxHostedServiceOptions
        {
            BatchSize = 0,
            PollInterval = TimeSpan.FromMilliseconds(10),
            MaxIterations = 1,
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task BackgroundService_WithZeroPollInterval_RefusesToStart()
    {
        var sut = CreateBackgroundService(new OutboxHostedServiceOptions
        {
            BatchSize = 10,
            PollInterval = TimeSpan.Zero,
            MaxIterations = 1,
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task BackgroundService_WithNonPositiveMaxIterations_RefusesToStart()
    {
        var sut = CreateBackgroundService(new OutboxHostedServiceOptions
        {
            BatchSize = 10,
            PollInterval = TimeSpan.FromMilliseconds(10),
            MaxIterations = 0,
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task BackgroundService_RunsMaxIterationsThenStops()
    {
        // Arrange
        var store = new FakeOutboxStore();
        store.Unpublished.Add(MakeMessage("a"));
        var sut = CreateBackgroundService(
            new OutboxHostedServiceOptions
            {
                BatchSize = 10,
                PollInterval = TimeSpan.FromMilliseconds(10),
                MaxIterations = 3,
            },
            store);

        // Act
        await sut.RunAsync(CancellationToken.None);

        // Assert — exactly MaxIterations polls, no more
        Assert.Equal(3, store.GetUnpublishedCalls);
    }

    [Fact]
    public async Task BackgroundService_WhenADispatchCycleThrows_KeepsTicking()
    {
        // Arrange — every poll throws; the loop must survive and still stop after MaxIterations
        var store = new FakeOutboxStore { FailGetUnpublished = true };
        var logger = new RecordingLogger<OutboxDispatchBackgroundService>();
        var sut = CreateBackgroundService(
            new OutboxHostedServiceOptions
            {
                BatchSize = 10,
                PollInterval = TimeSpan.FromMilliseconds(10),
                MaxIterations = 2,
            },
            store,
            logger);

        // Act
        await sut.RunAsync(CancellationToken.None);

        // Assert — two attempts, both logged as errors, and the service returned normally
        Assert.Equal(2, store.GetUnpublishedCalls);
        Assert.Equal(2, logger.Entries.Count(e => e.Level == LogLevel.Error));
    }

    private static TestableBackgroundService CreateBackgroundService(
        OutboxHostedServiceOptions options,
        FakeOutboxStore? store = null,
        RecordingLogger<OutboxDispatchBackgroundService>? logger = null)
    {
        var dispatch = new OutboxDispatchService(store ?? new FakeOutboxStore(), new FakePublisher());
        return new TestableBackgroundService(dispatch, options, logger);
    }

    private static OutboxMessage MakeMessage(string id) =>
        new(id, "agg-1", "TypeA", "{ }"u8.ToArray(), DateTimeOffset.UtcNow, false);


    /// <summary>Exposes <see cref="BackgroundService.ExecuteAsync"/> so validation and the loop can be awaited.</summary>
    private sealed class TestableBackgroundService(
        OutboxDispatchService dispatchService,
        OutboxHostedServiceOptions options,
        ILogger<OutboxDispatchBackgroundService>? logger = null)
        : OutboxDispatchBackgroundService(dispatchService, options, logger)
    {
        public Task RunAsync(CancellationToken cancellationToken) => ExecuteAsync(cancellationToken);
    }

    private sealed class FakePublisher : IOutboxPublisher
    {
        public List<string> Published { get; } = [];

        public HashSet<string> FailFor { get; } = [];

        public HashSet<string> CancelFor { get; } = [];

        public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            if (CancelFor.Contains(message.MessageId))
            {
                throw new OperationCanceledException("cancelled while publishing");
            }

            if (FailFor.Contains(message.MessageId))
            {
                throw new InvalidOperationException($"publish failed for {message.MessageId}");
            }

            Published.Add(message.MessageId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOutboxStore : IOutboxStore
    {
        public List<OutboxMessage> Unpublished { get; } = [];

        public List<OutboxMessage> DeadLetters { get; } = [];

        public List<string> MarkedPublished { get; } = [];

        public List<(string Id, string Error)> RecordedFailures { get; } = [];

        public int GetUnpublishedCalls { get; private set; }

        public bool FailGetUnpublished { get; init; }

        public Task<bool> AddAsync(OutboxMessage message, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<OutboxMessage>> GetUnpublishedAsync(int limit, CancellationToken cancellationToken = default)
        {
            GetUnpublishedCalls++;
            if (FailGetUnpublished)
            {
                throw new InvalidOperationException("store unavailable");
            }

            return Task.FromResult<IReadOnlyList<OutboxMessage>>(Unpublished.ToArray());
        }

        public Task MarkPublishedAsync(string messageId, CancellationToken cancellationToken = default)
        {
            MarkedPublished.Add(messageId);
            return Task.CompletedTask;
        }

        public Task RecordFailureAsync(string messageId, string error, CancellationToken cancellationToken = default)
        {
            RecordedFailures.Add((messageId, error));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboxMessage>> GetDeadLettersAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OutboxMessage>>(DeadLetters.Take(limit).ToArray());

        public Task RequeueDeadLetterAsync(string messageId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}

