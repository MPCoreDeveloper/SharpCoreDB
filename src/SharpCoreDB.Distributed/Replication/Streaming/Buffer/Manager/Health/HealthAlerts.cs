// <copyright file="HealthAlerts.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Threading.Channels;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Alert generation and notification system for buffer health issues.
/// Provides configurable alerting with severity-based notifications.
/// C# 14: Primary constructors, pattern matching, async event handling.
/// </summary>
public sealed class HealthAlerts : IAsyncDisposable
{
    private readonly Dictionary<string, AlertSubscription> _subscriptions = [];
    private readonly Channel<HealthAlert> _alertChannel = Channel.CreateBounded<HealthAlert>(100);
    private readonly Lock _alertsLock = new();

    private Task? _alertProcessorTask;
    private bool _isProcessing;

    /// <summary>
    /// Delegate for handling health alerts.
    /// </summary>
    /// <param name="alert">The health alert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public delegate Task AlertHandler(HealthAlert alert, CancellationToken cancellationToken);

    /// <summary>
    /// Subscribes to health alerts.
    /// </summary>
    /// <param name="minSeverity">Minimum severity level to alert on.</param>
    /// <param name="categories">Categories to monitor (empty for all).</param>
    /// <param name="handler">The alert handler.</param>
    /// <returns>A subscription that can be disposed to unsubscribe.</returns>
    public IDisposable Subscribe(
        HealthIssueSeverity minSeverity = HealthIssueSeverity.Warning,
        IReadOnlyCollection<HealthIssueCategory>? categories = null,
        AlertHandler? handler = null)
    {
        var subscription = new AlertSubscription(this, minSeverity, categories ?? [], handler);
        var key = subscription.Id;

        lock (_alertsLock)
        {
            _subscriptions[key] = subscription;
        }

        return subscription;
    }

    /// <summary>
    /// Raises a health alert.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="issue">The health issue.</param>
    /// <param name="assessment">The health assessment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RaiseAlertAsync(
        string bufferId,
        BufferHealthIssue issue,
        BufferHealthAssessment assessment,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferId);

        var alert = new HealthAlert
        {
            Id = Guid.NewGuid().ToString(),
            BufferId = bufferId,
            Issue = issue,
            Assessment = assessment,
            Timestamp = DateTimeOffset.UtcNow,
            IsResolved = false
        };

        await _alertChannel.Writer.WriteAsync(alert, cancellationToken);
    }

    /// <summary>
    /// Resolves an existing alert.
    /// </summary>
    /// <param name="alertId">The alert identifier.</param>
    /// <param name="resolution">The resolution description.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ResolveAlertAsync(
        string alertId,
        string resolution,
        CancellationToken cancellationToken = default)
    {
        var resolutionAlert = new HealthAlert
        {
            Id = alertId,
            Resolution = resolution,
            Timestamp = DateTimeOffset.UtcNow,
            IsResolved = true
        };

        await _alertChannel.Writer.WriteAsync(resolutionAlert, cancellationToken);
    }

    /// <summary>
    /// Starts the alert processing system.
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
        _alertProcessorTask = ProcessAlertsAsync(_cts.Token);
    }

    /// <summary>
    /// Stops the alert processing system.
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

        if (_alertProcessorTask is not null)
        {
            await _alertProcessorTask.WaitAsync(TimeSpan.FromSeconds(5));
        }

        _alertChannel.Writer.Complete();
    }

    /// <summary>
    /// Gets alert statistics.
    /// </summary>
    /// <returns>Alert statistics.</returns>
    public AlertStats GetStats()
    {
        lock (_alertsLock)
        {
            return new AlertStats
            {
                ActiveSubscriptions = _subscriptions.Count,
                PendingAlerts = _alertChannel.Reader.Count,
                IsProcessing = _isProcessing
            };
        }
    }

    /// <summary>
    /// Processes alerts asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the alert processing operation.</returns>
    private async Task ProcessAlertsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var alert in _alertChannel.Reader.ReadAllAsync(cancellationToken))
            {
                await ProcessAlertAsync(alert, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            // Log error but continue processing
            // _logger?.LogError(ex, "Error processing health alert");
        }
    }

    /// <summary>
    /// Processes a single alert.
    /// </summary>
    /// <param name="alert">The alert to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessAlertAsync(HealthAlert alert, CancellationToken cancellationToken)
    {
        List<AlertSubscription> matchingSubscriptions;

        lock (_alertsLock)
        {
            matchingSubscriptions = [.. GetMatchingSubscriptions(alert)];
        }

        // Process all matching subscriptions
        var tasks = matchingSubscriptions
            .Where(s => s.Handler is not null)
            .Select(s => ProcessSubscriptionAsync(s, alert, cancellationToken))
            .ToArray();

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Gets subscriptions that match the alert.
    /// </summary>
    /// <param name="alert">The alert.</param>
    /// <returns>Matching subscriptions.</returns>
    private IEnumerable<AlertSubscription> GetMatchingSubscriptions(HealthAlert alert)
    {
        foreach (var subscription in _subscriptions.Values)
        {
            if (ShouldDeliverAlert(subscription, alert))
            {
                yield return subscription;
            }
        }
    }

    /// <summary>
    /// Determines if an alert should be delivered to a subscription.
    /// </summary>
    /// <param name="subscription">The subscription.</param>
    /// <param name="alert">The alert.</param>
    /// <returns>True if the alert should be delivered.</returns>
    private static bool ShouldDeliverAlert(AlertSubscription subscription, HealthAlert alert)
    {
        // Check severity
        if (alert.IsResolved)
        {
            // Always deliver resolutions
            return true;
        }

        if (alert.Issue.Severity < subscription.MinSeverity)
        {
            return false;
        }

        // Check categories
        if (subscription.Categories.Count > 0 &&
            !subscription.Categories.Contains(alert.Issue.Category))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Processes a subscription for an alert.
    /// </summary>
    /// <param name="subscription">The subscription.</param>
    /// <param name="alert">The alert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task ProcessSubscriptionAsync(
        AlertSubscription subscription,
        HealthAlert alert,
        CancellationToken cancellationToken)
    {
        try
        {
            await subscription.Handler!(alert, cancellationToken);
        }
        catch (Exception ex)
        {
            // Log handler error but don't let it stop processing
            // _logger?.LogError(ex, "Error in alert handler for subscription {SubscriptionId}", subscription.Id);
        }
    }

    /// <summary>
    /// Unsubscribes a subscription.
    /// </summary>
    /// <param name="subscriptionId">The subscription identifier.</param>
    internal void Unsubscribe(string subscriptionId)
    {
        lock (_alertsLock)
        {
            _subscriptions.Remove(subscriptionId);
        }
    }

    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// Disposes the alerts system asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopProcessingAsync();
        _cts.Dispose();
        _alertChannel.Writer.Complete();
    }
}

/// <summary>
/// Health alert notification.
/// </summary>
public class HealthAlert
{
    /// <summary>Gets the unique alert identifier.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the buffer identifier.</summary>
    public string BufferId { get; init; } = string.Empty;

    /// <summary>Gets the health issue (null for resolutions).</summary>
    public BufferHealthIssue? Issue { get; init; }

    /// <summary>Gets the health assessment.</summary>
    public BufferHealthAssessment? Assessment { get; init; }

    /// <summary>Gets the alert timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets whether this is a resolution alert.</summary>
    public bool IsResolved { get; init; }

    /// <summary>Gets the resolution description (for resolution alerts).</summary>
    public string? Resolution { get; init; }

    /// <summary>Gets the alert priority based on severity.</summary>
    public AlertPriority Priority => IsResolved ? AlertPriority.Low :
        Issue?.Severity switch
        {
            HealthIssueSeverity.Critical => AlertPriority.Critical,
            HealthIssueSeverity.Error => AlertPriority.High,
            HealthIssueSeverity.Warning => AlertPriority.Medium,
            _ => AlertPriority.Low
        };

    /// <summary>Gets a human-readable alert message.</summary>
    public string Message => IsResolved
        ? $"Alert {Id} resolved: {Resolution}"
        : $"Buffer {BufferId}: {Issue?.Title} - {Issue?.Description}";
}

/// <summary>
/// Alert subscription.
/// </summary>
internal class AlertSubscription : IDisposable
{
    private readonly HealthAlerts _alerts;

    /// <summary>Gets the subscription identifier.</summary>
    public string Id { get; } = Guid.NewGuid().ToString();

    /// <summary>Gets the minimum severity level.</summary>
    public HealthIssueSeverity MinSeverity { get; }

    /// <summary>Gets the monitored categories.</summary>
    public IReadOnlyCollection<HealthIssueCategory> Categories { get; }

    /// <summary>Gets the alert handler.</summary>
    public HealthAlerts.AlertHandler? Handler { get; }

    public AlertSubscription(
        HealthAlerts alerts,
        HealthIssueSeverity minSeverity,
        IReadOnlyCollection<HealthIssueCategory> categories,
        HealthAlerts.AlertHandler? handler)
    {
        _alerts = alerts;
        MinSeverity = minSeverity;
        Categories = categories;
        Handler = handler;
    }

    /// <summary>
    /// Disposes the subscription.
    /// </summary>
    public void Dispose()
    {
        _alerts.Unsubscribe(Id);
    }
}

/// <summary>
/// Alert statistics.
/// </summary>
public class AlertStats
{
    /// <summary>Gets the number of active subscriptions.</summary>
    public int ActiveSubscriptions { get; init; }

    /// <summary>Gets the number of pending alerts.</summary>
    public int PendingAlerts { get; init; }

    /// <summary>Gets whether the system is processing alerts.</summary>
    public bool IsProcessing { get; init; }
}

/// <summary>
/// Alert priority levels.
/// </summary>
public enum AlertPriority
{
    /// <summary>Low priority alert.</summary>
    Low,

    /// <summary>Medium priority alert.</summary>
    Medium,

    /// <summary>High priority alert.</summary>
    High,

    /// <summary>Critical priority alert.</summary>
    Critical
}

/// <summary>
/// Builder for creating alert subscriptions with fluent API.
/// </summary>
public class HealthAlertSubscriptionBuilder
{
    private readonly HealthAlerts _alerts;

    public HealthAlertSubscriptionBuilder(HealthAlerts alerts)
    {
        _alerts = alerts ?? throw new ArgumentNullException(nameof(alerts));
    }

    /// <summary>
    /// Subscribes to critical alerts.
    /// </summary>
    /// <param name="handler">The alert handler.</param>
    /// <returns>The subscription.</returns>
    public IDisposable OnCritical(HealthAlerts.AlertHandler handler)
    {
        return _alerts.Subscribe(HealthIssueSeverity.Critical, handler: handler);
    }

    /// <summary>
    /// Subscribes to error alerts.
    /// </summary>
    /// <param name="handler">The alert handler.</param>
    /// <returns>The subscription.</returns>
    public IDisposable OnErrors(HealthAlerts.AlertHandler handler)
    {
        return _alerts.Subscribe(HealthIssueSeverity.Error, handler: handler);
    }

    /// <summary>
    /// Subscribes to warning alerts.
    /// </summary>
    /// <param name="handler">The alert handler.</param>
    /// <returns>The subscription.</returns>
    public IDisposable OnWarnings(HealthAlerts.AlertHandler handler)
    {
        return _alerts.Subscribe(HealthIssueSeverity.Warning, handler: handler);
    }

    /// <summary>
    /// Subscribes to alerts for specific categories.
    /// </summary>
    /// <param name="categories">The categories to monitor.</param>
    /// <param name="handler">The alert handler.</param>
    /// <returns>The subscription.</returns>
    public IDisposable ForCategories(HealthIssueCategory[] categories, HealthAlerts.AlertHandler handler)
    {
        return _alerts.Subscribe(categories: categories, handler: handler);
    }

    /// <summary>
    /// Subscribes to all alerts.
    /// </summary>
    /// <param name="handler">The alert handler.</param>
    /// <returns>The subscription.</returns>
    public IDisposable OnAll(HealthAlerts.AlertHandler handler)
    {
        return _alerts.Subscribe(handler: handler);
    }
}

/// <summary>
/// Extension methods for HealthAlerts.
/// </summary>
public static class HealthAlertsExtensions
{
    /// <summary>
    /// Creates a subscription builder for the alerts system.
    /// </summary>
    /// <param name="alerts">The alerts system.</param>
    /// <returns>A subscription builder.</returns>
    public static HealthAlertSubscriptionBuilder Subscribe(this HealthAlerts alerts)
    {
        return new HealthAlertSubscriptionBuilder(alerts);
    }
}
