// <copyright file="DiagnosticsSnapshot.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Viewer.Models;

/// <summary>
/// Immutable snapshot of database diagnostics collected at a point in time.
/// </summary>
public sealed record DiagnosticsSnapshot
{
    /// <summary>Total number of pages in the database file.</summary>
    public long PageCount { get; init; }

    /// <summary>Size of each database page in bytes.</summary>
    public long PageSize { get; init; }

    /// <summary>Computed total database size in bytes.</summary>
    public long TotalSizeBytes => PageCount * PageSize;

    /// <summary>Cache size in pages (negative values are kibibytes in SQLite).</summary>
    public long CacheSizePages { get; init; }

    /// <summary>Active journal mode (wal, delete, truncate, persist, memory, off).</summary>
    public string JournalMode { get; init; } = string.Empty;

    /// <summary>Result of PRAGMA integrity_check — "ok" when the database is healthy.</summary>
    public string IntegrityStatus { get; init; } = string.Empty;

    /// <summary>True when integrity_check returned "ok".</summary>
    public bool IsHealthy { get; init; }

    /// <summary>Row counts per table name at snapshot time.</summary>
    public IReadOnlyDictionary<string, long> TableRowCounts { get; init; } = new Dictionary<string, long>();

    /// <summary>UTC timestamp when this snapshot was captured.</summary>
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
