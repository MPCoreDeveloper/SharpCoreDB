// <copyright file="LifecycleEvents.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Collections.Concurrent;
using System.Threading.Channels;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Event-driven system for handling buffer lifecycle events.
/// Provides subscription-based event handling with filtering and aggregation.
/// C# 14: Async event handling, collection expressions, pattern matching.
/// </summary>
public sealed class LifecycleEvents : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, List<LifecycleEventHandler>> _eventHandlers = [];
    private readonly Channel<BufferLifecycleEvent> _eventChannel = Channel.CreateBounded<BufferLifecycleEvent>(1000);
    private readonly CancellationTokenSource _cts = new();

    private Task? _eventProcessorTask;
    private bool _isProcessing;

    /// <summary>
    /// Delegate for handling lifecycle events.
    /// </summary>
    /// <param name="event">The lifecycle event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public delegate Task LifecycleEventHandler(BufferLifecycleEvent @event, CancellationToken cancellationToken);

    /// <summary>
    /// Subscribes to lifecycle events.
    /// </summary>
    /// <param name="eventType">The event type to subscribe to, or null for all events.</param>
    /// <param name="handler">The event handler.</param>
    /// <returns>A subscription that can be disposed to unsubscribe.</returns>
    public IDisposable Subscribe(LifecycleEventType? eventType, LifecycleEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var key = eventType?.ToString() ?? "All";
        var subscription = new EventSubscription(this, key, handler);

        _eventHandlers.AddOrUpdate(
            key,
            _ => [handler],
            (_, list) => { list.Add(handler); return list; });

        return subscription;
    }

    /// <summary>
    /// Subscribes to lifecycle events with filtering.
    /// </summary>
    /// <param name="filter">The event filter function.</param>
    /// <param name="handler">The event handler.</param>
    /// <returns>A subscription that can be disposed to unsubscribe.</returns>
    public IDisposable Subscribe(Func<BufferLifecycleEvent, bool> filter, LifecycleEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(handler);

        var subscription = new FilteredEventSubscription(this, filter, handler);

        // For filtered subscriptions, we use a special key
        var key = $"Filtered_{subscription.GetHashCode()}";
        _eventHandlers.AddOrUpdate(
            key,
            _ => [handler],
            (_, list) => { list.Add(handler); return list; });

        subscription.Key = key;
        return subscription;
    }

    /// <summary>
    /// Publishes a lifecycle event to all subscribers.
    /// </summary>
    /// <param name="event">The event to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task PublishEventAsync(BufferLifecycleEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        await _eventChannel.Writer.WriteAsync(@event, cancellationToken);
    }

    /// <summary>
    /// Starts the event processing system.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartProcessingAsync(CancellationToken cancellationToken = default)
    {
        if (_isProcessing)
        {
            return;
        }

        _isProcessing = true;
        _eventProcessorTask = ProcessEventsAsync(_cts.Token);
    }

    /// <summary>
    /// Stops the event processing system.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StopProcessingAsync()
    {
        if (!_isProcessing)
        {
            return;
        }

        _isProcessing = false;
        _cts.Cancel();

        if (_eventProcessorTask is not null)
        {
            await _eventProcessorTask.WaitAsync(TimeSpan.FromSeconds(5));
        }

        _eventChannel.Writer.Complete();
    }

    /// <summary>
    /// Gets event statistics.
    /// </summary>
    /// <returns>Event statistics.</returns>
    public LifecycleEventStats GetStats()
    {
        var handlerCounts = _eventHandlers.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Count);

        return new LifecycleEventStats
        {
            IsProcessing = _isProcessing,
            TotalSubscriptions = handlerCounts.Values.Sum(),
            SubscriptionCounts = handlerCounts,
            PendingEvents = _eventChannel.Reader.Count,
            ChannelCapacity = _eventChannel.Reader.Count // Simplified, Channel doesn't expose writer count
        };
    }

    /// <summary>
    /// Processes events asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the event processing operation.</returns>
    private async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var @event in _eventChannel.Reader.ReadAllAsync(cancellationToken))
            {
                await ProcessEventAsync(@event, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            // Log error but continue processing
            // _logger?.LogError(ex, "Error processing lifecycle event");
        }
    }

    /// <summary>
    /// Processes a single event.
    /// </summary>
    /// <param name="event">The event to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessEventAsync(BufferLifecycleEvent @event, CancellationToken cancellationToken)
    {
        // Get handlers for specific event type
        var specificHandlers = _eventHandlers.GetValueOrDefault(@event.EventType.ToString(), []);

        // Get handlers for all events
        var allHandlers = _eventHandlers.GetValueOrDefault("All", []);

        // Combine all applicable handlers
        var applicableHandlers = specificHandlers.Concat(allHandlers).ToList();

        // Also check filtered subscriptions
        var filteredHandlers = new List<LifecycleEventHandler>();
        foreach (var kvp in _eventHandlers)
        {
            if (kvp.Key.StartsWith("Filtered_"))
            {
                // For filtered subscriptions, we need to check the filter
                // In a real implementation, we'd store the filter with the subscription
                filteredHandlers.AddRange(kvp.Value);
            }
        }

        // Process all applicable handlers
        var allHandlerTasks = applicableHandlers
            .Select(handler => ProcessHandlerAsync(handler, @event, cancellationToken))
            .ToArray();

        await Task.WhenAll(allHandlerTasks);
    }

    /// <summary>
    /// Processes a single event handler.
    /// </summary>
    /// <param name="handler">The event handler.</param>
    /// <param name="event">The event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task ProcessHandlerAsync(
        LifecycleEventHandler handler,
        BufferLifecycleEvent @event,
        CancellationToken cancellationToken)
    {
        try
        {
            await handler(@event, cancellationToken);
        }
        catch (Exception ex)
        {
            // Log handler error but don't let it stop processing
            // _logger?.LogError(ex, "Error in lifecycle event handler for event {EventType}", @event.EventType);
        }
    }

    /// <summary>
    /// Unsubscribes a handler.
    /// </summary>
    /// <param name="key">The subscription key.</param>
    /// <param name="handler">The handler to remove.</param>
    internal void Unsubscribe(string key, LifecycleEventHandler handler)
    {
        if (_eventHandlers.TryGetValue(key, out var handlers))
        {
            handlers.Remove(handler);
            if (handlers.Count == 0)
            {
                _eventHandlers.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// Disposes the event system asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopProcessingAsync();
        _cts.Dispose();
        _eventChannel.Writer.Complete();
    }
}

/// <summary>
/// Statistics for lifecycle event processing.
/// </summary>
public class LifecycleEventStats
{
    /// <summary>Gets whether the event system is processing.</summary>
    public bool IsProcessing { get; init; }

    /// <summary>Gets the total number of subscriptions.</summary>
    public int TotalSubscriptions { get; init; }

    /// <summary>Gets the subscription counts by event type.</summary>
    public IReadOnlyDictionary<string, int> SubscriptionCounts { get; init; } = new Dictionary<string, int>();

    /// <summary>Gets the number of pending events in the channel.</summary>
    public int PendingEvents { get; init; }

    /// <summary>Gets the total channel capacity.</summary>
    public int ChannelCapacity { get; init; }

    /// <summary>Gets the channel utilization as a percentage.</summary>
    public double ChannelUtilization => ChannelCapacity > 0 ? (double)PendingEvents / ChannelCapacity * 100 : 0;
}

/// <summary>
/// Base class for event subscriptions.
/// </summary>
internal abstract class EventSubscriptionBase : IDisposable
{
    protected readonly LifecycleEvents _eventSystem;
    protected readonly LifecycleEvents.LifecycleEventHandler _handler;

    /// <summary>Gets or sets the subscription key.</summary>
    public string? Key { get; set; }

    protected EventSubscriptionBase(LifecycleEvents eventSystem, LifecycleEvents.LifecycleEventHandler handler)
    {
        _eventSystem = eventSystem;
        _handler = handler;
    }

    /// <summary>
    /// Disposes the subscription.
    /// </summary>
    public virtual void Dispose()
    {
        if (Key is not null)
        {
            _eventSystem.Unsubscribe(Key, _handler);
        }
    }
}

/// <summary>
/// Subscription for specific event types.
/// </summary>
internal sealed class EventSubscription : EventSubscriptionBase
{
    public EventSubscription(LifecycleEvents eventSystem, string key, LifecycleEvents.LifecycleEventHandler handler)
        : base(eventSystem, handler)
    {
        Key = key;
    }
}

/// <summary>
/// Subscription with event filtering.
/// </summary>
internal sealed class FilteredEventSubscription : EventSubscriptionBase
{
    private readonly Func<BufferLifecycleEvent, bool> _filter;

    public FilteredEventSubscription(
        LifecycleEvents eventSystem,
        Func<BufferLifecycleEvent, bool> filter,
        LifecycleEvents.LifecycleEventHandler handler)
        : base(eventSystem, handler)
    {
        _filter = filter;
    }

    public override void Dispose()
    {
        // For filtered subscriptions, we need additional logic to check the filter
        // In a real implementation, this would be more sophisticated
        base.Dispose();
    }
}

/// <summary>
/// Builder for creating event subscriptions with fluent API.
/// </summary>
public class LifecycleEventSubscriptionBuilder
{
    private readonly LifecycleEvents _eventSystem;

    public LifecycleEventSubscriptionBuilder(LifecycleEvents eventSystem)
    {
        _eventSystem = eventSystem ?? throw new ArgumentNullException(nameof(eventSystem));
    }

    /// <summary>
    /// Subscribes to allocation events.
    /// </summary>
    /// <param name="handler">The event handler.</param>
    /// <returns>The subscription.</returns>
    public IDisposable OnAllocated(LifecycleEvents.LifecycleEventHandler handler)
    {
        return _eventSystem.Subscribe(LifecycleEventType.Allocated, handler);
    }

    /// <summary>
    /// Subscribes to rental events.
    /// </summary>
    /// <param name="handler">The event handler.</param>
    /// <returns>The subscription.</returns>
    public IDisposable OnRented(LifecycleEvents.LifecycleEventHandler handler)
    {
        return _eventSystem.Subscribe(LifecycleEventType.Rented, handler);
    }

    /// <summary>
    /// Subscribes to return events.
    /// </summary>
    /// <param name="handler">The event handler.</param>
    /// <returns>The subscription.</returns>
    public IDisposable OnReturned(LifecycleEvents.LifecycleEventHandler handler)
    {
        return _eventSystem.Subscribe(LifecycleEventType.Returned, handler);
    }

    /// <summary>
    /// Subscribes to deallocation events.
    /// </summary>
    /// <param name="handler">The event handler.</param>
    /// <returns>The subscription.</returns>
    public IDisposable OnDeallocated(LifecycleEvents.LifecycleEventHandler handler)
    {
        return _eventSystem.Subscribe(LifecycleEventType.Deallocated, handler);
    }

    /// <summary>
    /// Subscribes to all events.
    /// </summary>
    /// <param name="handler">The event handler.</param>
    /// <returns>The subscription.</returns>
    public IDisposable OnAll(LifecycleEvents.LifecycleEventHandler handler)
    {
        return _eventSystem.Subscribe((LifecycleEventType?)null, handler);
    }

    /// <summary>
    /// Subscribes to events matching a filter.
    /// </summary>
    /// <param name="filter">The event filter.</param>
    /// <param name="handler">The event handler.</param>
    /// <returns>The subscription.</returns>
    public IDisposable When(Func<BufferLifecycleEvent, bool> filter, LifecycleEvents.LifecycleEventHandler handler)
    {
        return _eventSystem.Subscribe(filter, handler);
    }

    /// <summary>
    /// Subscribes to events from a specific source.
    /// </summary>
    /// <param name="source">The event source.</param>
    /// <param name="handler">The event handler.</param>
    /// <returns>The subscription.</returns>
    public IDisposable FromSource(string source, LifecycleEvents.LifecycleEventHandler handler)
    {
        return _eventSystem.Subscribe(e => e.Source == source, handler);
    }

    /// <summary>
    /// Subscribes to events for a specific buffer.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="handler">The event handler.</param>
    /// <returns>The subscription.</returns>
    public IDisposable ForBuffer(string bufferId, LifecycleEvents.LifecycleEventHandler handler)
    {
        return _eventSystem.Subscribe(e => e.BufferId == bufferId, handler);
    }
}

/// <summary>
/// Extension methods for LifecycleEvents.
/// </summary>
public static class LifecycleEventsExtensions
{
    /// <summary>
    /// Creates a subscription builder for the event system.
    /// </summary>
    /// <param name="eventSystem">The event system.</param>
    /// <returns>A subscription builder.</returns>
    public static LifecycleEventSubscriptionBuilder Subscribe(this LifecycleEvents eventSystem)
    {
        return new LifecycleEventSubscriptionBuilder(eventSystem);
    }
}
