// <copyright file="DiagnosticsViewModel.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCoreDB.Data.Provider;
using SharpCoreDB.Viewer.Models;
using SharpCoreDB.Viewer.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace SharpCoreDB.Viewer.ViewModels;

/// <summary>
/// ViewModel for the Diagnostics dialog — collects PRAGMA-based storage stats,
/// integrity health, table row counts, and exposes safe admin maintenance commands.
/// </summary>
public partial class DiagnosticsViewModel : ViewModelBase
{
    private readonly LocalizationService _localization = LocalizationService.Instance;
    private DiagnosticsSnapshot? _lastSnapshot;

    /// <summary>Active database connection; must be set before RunDiagnosticsCommand is invoked.</summary>
    public SharpCoreDBConnection? Connection { get; set; }

    /// <summary>Avalonia storage provider for the export file picker.</summary>
    public IStorageProvider? StorageProvider { get; set; }

    [ObservableProperty]
    private long _pageCount;

    [ObservableProperty]
    private long _pageSize;

    [ObservableProperty]
    private string _totalSize = string.Empty;

    [ObservableProperty]
    private long _cacheSizePages;

    [ObservableProperty]
    private string _journalMode = string.Empty;

    [ObservableProperty]
    private string _integrityStatus = string.Empty;

    [ObservableProperty]
    private bool _isHealthy;

    [ObservableProperty]
    private ObservableCollection<string> _tableRowCounts = [];

    [ObservableProperty]
    private string _capturedAt = string.Empty;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _hasDiagnostics;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _adminActionComplete;

    /// <summary>True when the currently displayed data was loaded from a file rather than a live connection.</summary>
    [ObservableProperty]
    private bool _isImportedSnapshot;

    partial void OnHasDiagnosticsChanged(bool value)
    {
        ExportSnapshotCommand.NotifyCanExecuteChanged();
        OptimizeDatabaseCommand.NotifyCanExecuteChanged();
        CheckpointWalCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRunningChanged(bool value)
    {
        ExportSnapshotCommand.NotifyCanExecuteChanged();
        OptimizeDatabaseCommand.NotifyCanExecuteChanged();
        CheckpointWalCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsImportedSnapshotChanged(bool value)
    {
        OptimizeDatabaseCommand.NotifyCanExecuteChanged();
        CheckpointWalCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Collects all diagnostic data from the active connection.</summary>
    [RelayCommand]
    private async Task RunDiagnosticsAsync()
    {
        if (Connection is null)
        {
            ErrorMessage = _localization["DiagnosticsNotConnected"];
            return;
        }

        IsRunning = true;
        ErrorMessage = string.Empty;
        StatusMessage = _localization["DiagnosticsRunning"];
        AdminActionComplete = false;

        try
        {
            var pageCount = await ReadPragmaLongAsync("page_count").ConfigureAwait(true);
            var pageSize = await ReadPragmaLongAsync("page_size").ConfigureAwait(true);
            var cacheSizePages = await ReadPragmaLongAsync("cache_size").ConfigureAwait(true);
            var journalMode = await ReadPragmaStringAsync("journal_mode").ConfigureAwait(true);
            var integrityStatus = await ReadIntegrityCheckAsync().ConfigureAwait(true);
            var tableCounts = await ReadTableRowCountsAsync().ConfigureAwait(true);

            var snapshot = new DiagnosticsSnapshot
            {
                PageCount = pageCount,
                PageSize = pageSize,
                CacheSizePages = cacheSizePages,
                JournalMode = journalMode,
                IntegrityStatus = integrityStatus,
                IsHealthy = integrityStatus.Equals("ok", StringComparison.OrdinalIgnoreCase),
                TableRowCounts = tableCounts,
                CapturedAtUtc = DateTimeOffset.UtcNow
            };

            _lastSnapshot = snapshot;

            IsImportedSnapshot = false;
            ApplySnapshotToDisplay(snapshot);
            HasDiagnostics = true;
            StatusMessage = _localization.Format("DiagnosticsComplete", CapturedAt);
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("ErrorDiagnosticsFailed", ex.Message);
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>Runs PRAGMA optimize to refresh query planner statistics.</summary>
    [RelayCommand(CanExecute = nameof(CanRunAdminAction))]
    private async Task OptimizeDatabaseAsync()
    {
        await ExecuteAdminPragmaAsync("optimize", "DiagnosticsOptimizeComplete").ConfigureAwait(true);
    }

    /// <summary>Flushes the WAL file to the main database (PRAGMA wal_checkpoint(FULL)).</summary>
    [RelayCommand(CanExecute = nameof(CanRunAdminAction))]
    private async Task CheckpointWalAsync()
    {
        if (Connection is null) return;

        IsRunning = true;
        ErrorMessage = string.Empty;
        AdminActionComplete = false;

        try
        {
            using var command = new SharpCoreDBCommand("PRAGMA wal_checkpoint(FULL)", Connection);
            using var reader = await command.ExecuteReaderAsync().ConfigureAwait(true);

            long log = 0, checkpointed = 0;
            if (await reader.ReadAsync().ConfigureAwait(true))
            {
                log = reader.GetValue(1) is long l ? l : 0;
                checkpointed = reader.GetValue(2) is long c ? c : 0;
            }

            AdminActionComplete = true;
            StatusMessage = _localization.Format("DiagnosticsCheckpointComplete", log, checkpointed);
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("ErrorActionFailed", ex.Message);
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>Exports the last diagnostics snapshot to a user-selected JSON file.</summary>
    [RelayCommand(CanExecute = nameof(CanExportSnapshot))]
    private async Task ExportSnapshotAsync()
    {
        if (StorageProvider is null || _lastSnapshot is null) return;

        var options = new FilePickerSaveOptions
        {
            Title = _localization["ExportSnapshotTitle"],
            SuggestedFileName = $"diagnostics-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json",
            DefaultExtension = ".json",
            FileTypeChoices =
            [
                new FilePickerFileType("JSON") { Patterns = ["*.json"] }
            ]
        };

        var file = await StorageProvider.SaveFilePickerAsync(options).ConfigureAwait(true);
        if (file is null) return;

        IsRunning = true;
        ErrorMessage = string.Empty;
        AdminActionComplete = false;
        StatusMessage = _localization["DiagnosticsExporting"];

        try
        {
            var exportData = new
            {
                capturedAt = _lastSnapshot.CapturedAtUtc,
                storage = new
                {
                    pageCount = _lastSnapshot.PageCount,
                    pageSizeBytes = _lastSnapshot.PageSize,
                    totalSizeBytes = _lastSnapshot.TotalSizeBytes,
                    cacheSizePages = _lastSnapshot.CacheSizePages,
                    journalMode = _lastSnapshot.JournalMode
                },
                health = new
                {
                    isHealthy = _lastSnapshot.IsHealthy,
                    integrityStatus = _lastSnapshot.IntegrityStatus
                },
                tableRowCounts = _lastSnapshot.TableRowCounts
            };

            await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);
            await JsonSerializer.SerializeAsync(stream, exportData, new JsonSerializerOptions { WriteIndented = true }).ConfigureAwait(true);

            AdminActionComplete = true;
            StatusMessage = _localization.Format("DiagnosticsExported", file.Name);
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("ErrorExportFailed", ex.Message);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool CanRunAdminAction() => HasDiagnostics && !IsRunning && !IsImportedSnapshot;

    private bool CanExportSnapshot() => HasDiagnostics && !IsRunning;

    private async Task ExecuteAdminPragmaAsync(string pragma, string successKey)
    {
        if (Connection is null) return;

        IsRunning = true;
        ErrorMessage = string.Empty;
        AdminActionComplete = false;

        try
        {
            using var command = new SharpCoreDBCommand($"PRAGMA {pragma}", Connection);
            await command.ExecuteNonQueryAsync().ConfigureAwait(true);
            AdminActionComplete = true;
            StatusMessage = _localization[successKey];
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("ErrorActionFailed", ex.Message);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task<long> ReadPragmaLongAsync(string pragma)
    {
        using var command = new SharpCoreDBCommand($"PRAGMA {pragma}", Connection!);
        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(true);
        if (await reader.ReadAsync().ConfigureAwait(true))
        {
            return reader.GetValue(0) switch
            {
                long l => l,
                int i => i,
                string s when long.TryParse(s, out var parsed) => parsed,
                _ => 0L
            };
        }
        return 0L;
    }

    private async Task<string> ReadPragmaStringAsync(string pragma)
    {
        using var command = new SharpCoreDBCommand($"PRAGMA {pragma}", Connection!);
        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(true);
        if (await reader.ReadAsync().ConfigureAwait(true))
        {
            return reader.GetValue(0)?.ToString() ?? string.Empty;
        }
        return string.Empty;
    }

    private async Task<string> ReadIntegrityCheckAsync()
    {
        using var command = new SharpCoreDBCommand("PRAGMA integrity_check", Connection!);
        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(true);
        if (await reader.ReadAsync().ConfigureAwait(true))
        {
            return reader.GetValue(0)?.ToString() ?? string.Empty;
        }
        return string.Empty;
    }

    private async Task<IReadOnlyDictionary<string, long>> ReadTableRowCountsAsync()
    {
        var tableNames = new List<string>();
        using (var cmd = new SharpCoreDBCommand(
            "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name", Connection!))
        using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(true))
        {
            while (await reader.ReadAsync().ConfigureAwait(true))
            {
                var name = reader.GetValue(0)?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                    tableNames.Add(name);
            }
        }

        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tableNames)
        {
            var escaped = table.Replace("'", "''", StringComparison.Ordinal);
            using var countCmd = new SharpCoreDBCommand($"SELECT COUNT(*) FROM '{escaped}'", Connection!);
            using var countReader = await countCmd.ExecuteReaderAsync().ConfigureAwait(true);
            if (await countReader.ReadAsync().ConfigureAwait(true))
            {
                counts[table] = countReader.GetValue(0) switch
                {
                    long l => l,
                    int i => i,
                    string s when long.TryParse(s, out var parsed) => parsed,
                    _ => 0L
                };
            }
        }
        return counts;
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F2} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };

    /// <summary>Imports a previously exported diagnostics snapshot from a JSON file.</summary>
    [RelayCommand]
    private async Task ImportSnapshotAsync()
    {
        if (StorageProvider is null) return;

        var options = new FilePickerOpenOptions
        {
            Title = _localization["ImportSnapshotTitle"],
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("JSON") { Patterns = ["*.json"] }
            ]
        };

        var files = await StorageProvider.OpenFilePickerAsync(options).ConfigureAwait(true);
        if (files.Count == 0) return;

        IsRunning = true;
        ErrorMessage = string.Empty;
        AdminActionComplete = false;

        try
        {
            await using var stream = await files[0].OpenReadAsync().ConfigureAwait(true);
            using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(true);
            var root = doc.RootElement;

            var capturedAt = root.TryGetProperty("capturedAt", out var capturedAtEl)
                ? capturedAtEl.GetDateTimeOffset()
                : DateTimeOffset.UtcNow;

            var storage = root.GetProperty("storage");
            var health = root.GetProperty("health");

            long pageCount = storage.GetProperty("pageCount").GetInt64();
            long pageSize = storage.GetProperty("pageSizeBytes").GetInt64();
            long cacheSizePages = storage.GetProperty("cacheSizePages").GetInt64();
            string journalMode = storage.GetProperty("journalMode").GetString() ?? string.Empty;
            bool isHealthy = health.GetProperty("isHealthy").GetBoolean();
            string integrityStatus = health.GetProperty("integrityStatus").GetString() ?? string.Empty;

            var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("tableRowCounts", out var tableCountsEl))
            {
                foreach (var prop in tableCountsEl.EnumerateObject())
                {
                    counts[prop.Name] = prop.Value.GetInt64();
                }
            }

            var snapshot = new DiagnosticsSnapshot
            {
                PageCount = pageCount,
                PageSize = pageSize,
                CacheSizePages = cacheSizePages,
                JournalMode = journalMode,
                IntegrityStatus = integrityStatus,
                IsHealthy = isHealthy,
                TableRowCounts = counts,
                CapturedAtUtc = capturedAt
            };

            _lastSnapshot = snapshot;
            IsImportedSnapshot = true;
            ApplySnapshotToDisplay(snapshot);
            HasDiagnostics = true;
            AdminActionComplete = true;
            StatusMessage = _localization.Format("DiagnosticsImported", files[0].Name);
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("ErrorImportFailed", ex.Message);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void ApplySnapshotToDisplay(DiagnosticsSnapshot snapshot)
    {
        PageCount = snapshot.PageCount;
        PageSize = snapshot.PageSize;
        TotalSize = FormatBytes(snapshot.TotalSizeBytes);
        CacheSizePages = snapshot.CacheSizePages;
        JournalMode = snapshot.JournalMode;
        IntegrityStatus = snapshot.IntegrityStatus;
        IsHealthy = snapshot.IsHealthy;
        CapturedAt = snapshot.CapturedAtUtc.ToLocalTime().ToString("G", CultureInfo.CurrentCulture);

        TableRowCounts.Clear();
        foreach (var (table, count) in snapshot.TableRowCounts.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            TableRowCounts.Add(_localization.Format("DiagnosticsRowCountFormat", table, count));
        }
    }
}
