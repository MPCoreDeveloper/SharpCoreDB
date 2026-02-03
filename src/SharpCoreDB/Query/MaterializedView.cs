// <copyright file="MaterializedView.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Query;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Materialized view for pre-computed query results.
/// C# 14: Record types, modern patterns, refresh strategies.
/// 
/// ✅ SCDB Phase 7.5: Advanced Query Optimization - Materialized Views
/// 
/// Purpose:
/// - Cache query results for fast access
/// - Automatic or manual refresh
/// - Query rewriting to use views
/// - Staleness tracking
/// </summary>
public sealed class MaterializedView<T> : IRefreshable, IDisposable
{
    private readonly string _name;
    private readonly Func<IEnumerable<T>> _queryFunction;
    private readonly RefreshStrategy _refreshStrategy;
    private readonly Lock _lock = new();
    private List<T>? _cachedData;
    private DateTime? _lastRefresh;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MaterializedView{T}"/> class.
    /// </summary>
    public MaterializedView(
        string name,
        Func<IEnumerable<T>> queryFunction,
        RefreshStrategy refreshStrategy = RefreshStrategy.Manual)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(queryFunction);

        _name = name;
        _queryFunction = queryFunction;
        _refreshStrategy = refreshStrategy;
    }

    /// <summary>Gets the view name.</summary>
    public string Name => _name;

    /// <summary>Gets when the view was last refreshed.</summary>
    public DateTime? LastRefresh => _lastRefresh;

    /// <summary>Gets whether the view has been initialized.</summary>
    public bool IsInitialized => _cachedData != null;

    /// <summary>Gets the number of cached rows.</summary>
    public int RowCount => _cachedData?.Count ?? 0;

    /// <summary>
    /// Refreshes the view by re-executing the query.
    /// </summary>
    public void Refresh()
    {
        lock (_lock)
        {
            _cachedData = _queryFunction().ToList();
            _lastRefresh = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Gets the cached data (refreshes if needed).
    /// </summary>
    public IEnumerable<T> GetData()
    {
        lock (_lock)
        {
            // Auto-refresh if needed
            if (ShouldRefresh())
            {
                Refresh();
            }

            if (_cachedData == null)
            {
                Refresh();
            }

            return _cachedData!.ToList(); // Return a copy
        }
    }

    /// <summary>
    /// Checks if the view is stale and needs refresh.
    /// </summary>
    public bool IsStale(TimeSpan maxAge)
    {
        if (_lastRefresh == null)
            return true;

        return DateTime.UtcNow - _lastRefresh.Value > maxAge;
    }

    /// <summary>
    /// Invalidates the view (marks for refresh).
    /// </summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            _lastRefresh = null;
        }
    }

    /// <summary>
    /// Gets view statistics.
    /// </summary>
    public MaterializedViewStats GetStats()
    {
        lock (_lock)
        {
            var age = _lastRefresh.HasValue
                ? DateTime.UtcNow - _lastRefresh.Value
                : TimeSpan.MaxValue;

            return new MaterializedViewStats
            {
                Name = _name,
                RowCount = RowCount,
                LastRefresh = _lastRefresh,
                Age = age,
                RefreshStrategy = _refreshStrategy,
                IsStale = ShouldRefresh()
            };
        }
    }

    // Private helpers

    private bool ShouldRefresh()
    {
        return _refreshStrategy switch
        {
            RefreshStrategy.Manual => false,
            RefreshStrategy.OnAccess => _cachedData == null,
            RefreshStrategy.Periodic => _lastRefresh == null || IsStale(TimeSpan.FromMinutes(5)),
            RefreshStrategy.Immediate => true,
            _ => false
        };
    }

    /// <summary>
    /// Disposes the view.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        lock (_lock)
        {
            _cachedData?.Clear();
            _cachedData = null;
        }

        _disposed = true;
    }
}

/// <summary>
/// Materialized view manager.
/// </summary>
public sealed class MaterializedViewManager : IDisposable
{
    private readonly Dictionary<string, object> _views = [];
    private readonly Lock _lock = new();
    private bool _disposed;

    /// <summary>Gets the number of registered views.</summary>
    public int ViewCount => _views.Count;

    /// <summary>
    /// Registers a materialized view.
    /// </summary>
    public void RegisterView<T>(MaterializedView<T> view)
    {
        ArgumentNullException.ThrowIfNull(view);

        lock (_lock)
        {
            _views[view.Name] = view;
        }
    }

    /// <summary>
    /// Gets a view by name.
    /// </summary>
    public MaterializedView<T>? GetView<T>(string name)
    {
        lock (_lock)
        {
            return _views.TryGetValue(name, out var view)
                ? view as MaterializedView<T>
                : null;
        }
    }

    /// <summary>
    /// Refreshes all views.
    /// </summary>
    public void RefreshAll()
    {
        lock (_lock)
        {
            foreach (var view in _views.Values)
            {
                if (view is IRefreshable refreshable)
                {
                    refreshable.Refresh();
                }
            }
        }
    }

    /// <summary>
    /// Removes a view.
    /// </summary>
    public bool RemoveView(string name)
    {
        lock (_lock)
        {
            if (_views.TryGetValue(name, out var view))
            {
                if (view is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                return _views.Remove(name);
            }

            return false;
        }
    }

    /// <summary>
    /// Gets all view names.
    /// </summary>
    public IEnumerable<string> GetViewNames()
    {
        lock (_lock)
        {
            return _views.Keys.ToList();
        }
    }

    /// <summary>
    /// Disposes the manager.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        lock (_lock)
        {
            foreach (var view in _views.Values)
            {
                if (view is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }

            _views.Clear();
        }

        _disposed = true;
    }
}

/// <summary>
/// Refresh strategy for materialized views.
/// </summary>
public enum RefreshStrategy
{
    /// <summary>Manual refresh only.</summary>
    Manual,

    /// <summary>Refresh on first access.</summary>
    OnAccess,

    /// <summary>Periodic automatic refresh.</summary>
    Periodic,

    /// <summary>Refresh on every access.</summary>
    Immediate
}

/// <summary>
/// Materialized view statistics.
/// </summary>
public sealed record MaterializedViewStats
{
    /// <summary>View name.</summary>
    public required string Name { get; init; }

    /// <summary>Number of cached rows.</summary>
    public required int RowCount { get; init; }

    /// <summary>Last refresh time.</summary>
    public required DateTime? LastRefresh { get; init; }

    /// <summary>Age of cached data.</summary>
    public required TimeSpan Age { get; init; }

    /// <summary>Refresh strategy.</summary>
    public required RefreshStrategy RefreshStrategy { get; init; }

    /// <summary>Whether the view is stale.</summary>
    public required bool IsStale { get; init; }
}

/// <summary>
/// Interface for refreshable views.
/// </summary>
internal interface IRefreshable
{
    void Refresh();
}
