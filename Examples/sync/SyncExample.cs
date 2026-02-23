// <copyright file="SyncExample.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Dotmim.Sync;
using Dotmim.Sync.SqlServer;
using SharpCoreDB.Provider.Sync;
using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Examples;

/// <summary>
/// Example: Bidirectional synchronization between SharpCoreDB and SQL Server using Dotmim.Sync.
/// This demonstrates how to sync data between a local SharpCoreDB instance and a remote SQL Server.
/// </summary>
public static class SyncExample
{
    /// <summary>
    /// Configures and runs bidirectional synchronization between SharpCoreDB and SQL Server.
    /// </summary>
    public static async Task RunSyncExampleAsync()
    {
        // Configure SharpCoreDB as local provider
        var sharpcoredbConnectionString = "Data Source=local.db";
        var sharpcoredbProvider = new SharpCoreDBSyncProvider(sharpcoredbConnectionString);

        // Configure SQL Server as remote provider
        var sqlServerConnectionString = "Server=myserver;Database=mydb;Trusted_Connection=True;";
        var sqlServerProvider = new SqlSyncProvider(sqlServerConnectionString);

        // Define tables to sync
        var tables = new string[] { "Users", "Orders", "Products" };

        // Create sync agent
        var agent = new SyncAgent(
            clientProvider: sharpcoredbProvider,  // SharpCoreDB as client
            serverProvider: sqlServerProvider     // SQL Server as server
        );

        // Configure sync options
        var options = new SyncOptions
        {
            BatchSize = 1000,  // Process in batches for performance
            UseBulkOperations = true,
            ConflictResolutionPolicy = ConflictResolutionPolicy.ServerWins
        };

        // Add logger for monitoring
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        agent.LocalOrchestrator.OnSyncProgress += (args) =>
        {
            Console.WriteLine($"Sync Progress: {args.ProgressPercentage}% - {args.Message}");
        };

        try
        {
            Console.WriteLine("Starting synchronization...");

            // Perform bidirectional sync
            var result = await agent.SynchronizeAsync(tables, options);

            Console.WriteLine("Synchronization completed!");
            Console.WriteLine($"Total changes downloaded: {result.TotalChangesDownloaded}");
            Console.WriteLine($"Total changes uploaded: {result.TotalChangesUploaded}");
            Console.WriteLine($"Total conflicts resolved: {result.TotalResolvedConflicts}");

            // Show sync details
            foreach (var table in result.ChangesAppliedOnClient)
            {
                Console.WriteLine($"Table {table.TableName}: {table.Applied} applied, {table.ResolvedConflicts} conflicts resolved");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Synchronization failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Example with multi-tenant filtering for local-first AI agent architectures.
    /// </summary>
    public static async Task RunMultiTenantSyncExampleAsync()
    {
        var sharpcoredbConnectionString = "Data Source=agent.db";
        var sharpcoredbProvider = new SharpCoreDBSyncProvider(sharpcoredbConnectionString);

        var sqlServerConnectionString = "Server=cloud;Database=agents;Trusted_Connection=True;";
        var sqlServerProvider = new SqlSyncProvider(sqlServerConnectionString);

        // Create sync agent with tenant filtering
        var agent = new SyncAgent(
            clientProvider: sharpcoredbProvider,
            serverProvider: sqlServerProvider
        );

        // Configure tenant-specific sync
        var tenantId = "tenant-123";
        var options = new SyncOptions
        {
            BatchSize = 500,
            UseBulkOperations = true
        };

        // Add tenant filter - only sync data for this tenant
        agent.AddFilter("TenantId", tenantId);

        // Define tenant-scoped tables
        var tables = new string[] { "Conversations", "Documents", "UserPreferences" };

        try
        {
            Console.WriteLine($"Starting tenant-specific sync for {tenantId}...");

            var result = await agent.SynchronizeAsync(tables, options);

            Console.WriteLine($"Tenant sync completed for {tenantId}!");
            Console.WriteLine($"Changes: ↑{result.TotalChangesUploaded} ↓{result.TotalChangesDownloaded}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Tenant sync failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Example of continuous background synchronization.
    /// </summary>
    public static async Task RunContinuousSyncExampleAsync()
    {
        var sharpcoredbConnectionString = "Data Source=continuous.db";
        var sharpcoredbProvider = new SharpCoreDBSyncProvider(sharpcoredbConnectionString);

        var sqlServerConnectionString = "Server=remote;Database=shared;Trusted_Connection=True;";
        var sqlServerProvider = new SqlSyncProvider(sqlServerConnectionString);

        var agent = new SyncAgent(sharpcoredbProvider, sqlServerProvider);
        var tables = new string[] { "SharedData", "UserSessions" };

        using var cts = new CancellationTokenSource();

        // Start continuous sync in background
        var syncTask = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var result = await agent.SynchronizeAsync(tables);
                    Console.WriteLine($"Background sync: ↑{result.TotalChangesUploaded} ↓{result.TotalChangesDownloaded}");

                    // Wait before next sync
                    await Task.Delay(TimeSpan.FromMinutes(5), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Background sync error: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(30), cts.Token); // Retry delay
                }
            }
        }, cts.Token);

        Console.WriteLine("Continuous sync started. Press Ctrl+C to stop.");

        // Wait for cancellation
        var cancelTask = Task.Run(() =>
        {
            Console.ReadKey();
            cts.Cancel();
        });

        await Task.WhenAny(syncTask, cancelTask);
        Console.WriteLine("Continuous sync stopped.");
    }
}
