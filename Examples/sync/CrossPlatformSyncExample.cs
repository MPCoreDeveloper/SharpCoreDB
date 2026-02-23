// <copyright file="CrossPlatformSyncExample.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Dotmim.Sync;
using Dotmim.Sync.SqlServer;
using Dotmim.Sync.PostgreSql;
using Dotmim.Sync.MySql;
using Dotmim.Sync.Sqlite;
using SharpCoreDB.Provider.Sync;
using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Examples;

/// <summary>
/// Example: Cross-platform synchronization between SharpCoreDB and various databases.
/// Demonstrates how SharpCoreDB can sync with SQL Server, PostgreSQL, MySQL, and SQLite.
/// </summary>
public static class CrossPlatformSyncExample
{
    /// <summary>
    /// Sync SharpCoreDB with SQL Server.
    /// </summary>
    public static async Task SyncWithSqlServerAsync()
    {
        var sharpcoredbProvider = new SharpCoreDBSyncProvider("Data Source=local.db");
        var sqlServerProvider = new SqlSyncProvider("Server=mssql;Database=sync;Trusted_Connection=True;");

        var agent = new SyncAgent(sharpcoredbProvider, sqlServerProvider);
        var result = await agent.SynchronizeAsync(["Users", "Orders", "Products"]);

        Console.WriteLine($"SharpCoreDB ↔️ SQL Server: ↑{result.TotalChangesUploaded} ↓{result.TotalChangesDownloaded}");
    }

    /// <summary>
    /// Sync SharpCoreDB with PostgreSQL.
    /// </summary>
    public static async Task SyncWithPostgreSqlAsync()
    {
        var sharpcoredbProvider = new SharpCoreDBSyncProvider("Data Source=local.db");
        var postgresProvider = new PostgreSqlSyncProvider("Server=postgres;Database=sync;User Id=user;Password=pass;");

        var agent = new SyncAgent(sharpcoredbProvider, postgresProvider);
        var result = await agent.SynchronizeAsync(["Users", "Orders", "Products"]);

        Console.WriteLine($"SharpCoreDB ↔️ PostgreSQL: ↑{result.TotalChangesUploaded} ↓{result.TotalChangesDownloaded}");
    }

    /// <summary>
    /// Sync SharpCoreDB with MySQL.
    /// </summary>
    public static async Task SyncWithMySqlAsync()
    {
        var sharpcoredbProvider = new SharpCoreDBSyncProvider("Data Source=local.db");
        var mysqlProvider = new MySqlSyncProvider("Server=mysql;Database=sync;Uid=user;Pwd=pass;");

        var agent = new SyncAgent(sharpcoredbProvider, mysqlProvider);
        var result = await agent.SynchronizeAsync(["Users", "Orders", "Products"]);

        Console.WriteLine($"SharpCoreDB ↔️ MySQL: ↑{result.TotalChangesUploaded} ↓{result.TotalChangesDownloaded}");
    }

    /// <summary>
    /// Sync SharpCoreDB with SQLite.
    /// </summary>
    public static async Task SyncWithSqliteAsync()
    {
        var sharpcoredbProvider = new SharpCoreDBSyncProvider("Data Source=local.db");
        var sqliteProvider = new SqliteSyncProvider("Data Source=remote.db");

        var agent = new SyncAgent(sharpcoredbProvider, sqliteProvider);
        var result = await agent.SynchronizeAsync(["Users", "Orders", "Products"]);

        Console.WriteLine($"SharpCoreDB ↔️ SQLite: ↑{result.TotalChangesUploaded} ↓{result.TotalChangesDownloaded}");
    }

    /// <summary>
    /// Multi-way sync: SharpCoreDB as central hub syncing with multiple databases.
    /// </summary>
    public static async Task MultiWaySyncExampleAsync()
    {
        var sharpcoredbProvider = new SharpCoreDBSyncProvider("Data Source=hub.db");

        // Sync with SQL Server
        var sqlServerProvider = new SqlSyncProvider("Server=mssql;Database=hub;Trusted_Connection=True;");
        var sqlServerAgent = new SyncAgent(sharpcoredbProvider, sqlServerProvider);

        // Sync with PostgreSQL
        var postgresProvider = new PostgreSqlSyncProvider("Server=postgres;Database=hub;User Id=user;Password=pass;");
        var postgresAgent = new SyncAgent(sharpcoredbProvider, postgresProvider);

        // Sync with MySQL
        var mysqlProvider = new MySqlSyncProvider("Server=mysql;Database=hub;Uid=user;Pwd=pass;");
        var mysqlAgent = new SyncAgent(sharpcoredbProvider, mysqlProvider);

        var tables = new[] { "Users", "Orders", "Products", "Inventory" };

        // Sync all databases with SharpCoreDB as the central hub
        var results = await Task.WhenAll(
            sqlServerAgent.SynchronizeAsync(tables),
            postgresAgent.SynchronizeAsync(tables),
            mysqlAgent.SynchronizeAsync(tables)
        );

        Console.WriteLine("Multi-way sync completed:");
        Console.WriteLine($"SQL Server: ↑{results[0].TotalChangesUploaded} ↓{results[0].TotalChangesDownloaded}");
        Console.WriteLine($"PostgreSQL: ↑{results[1].TotalChangesUploaded} ↓{results[1].TotalChangesDownloaded}");
        Console.WriteLine($"MySQL: ↑{results[2].TotalChangesUploaded} ↓{results[2].TotalChangesDownloaded}");
    }

    /// <summary>
    /// Enterprise sync with monitoring and error handling.
    /// </summary>
    public static async Task EnterpriseSyncExampleAsync()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var sharpcoredbProvider = new SharpCoreDBSyncProvider("Data Source=enterprise.db");
        var sqlServerProvider = new SqlSyncProvider("Server=prod-sql;Database=enterprise;Trusted_Connection=True;");

        var agent = new SyncAgent(sharpcoredbProvider, sqlServerProvider);

        // Configure enterprise settings
        var options = new SyncOptions
        {
            BatchSize = 5000,
            UseBulkOperations = true,
            UseCompression = true,
            ConflictResolutionPolicy = ConflictResolutionPolicy.ServerWins,
            MaxRetries = 5,
            UseVerboseErrors = true
        };

        // Progress monitoring
        agent.LocalOrchestrator.OnSyncProgress += (args) =>
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {args.ProgressPercentage}% - {args.Message}");
        };

        // Error handling
        agent.LocalOrchestrator.OnSyncError += (args) =>
        {
            Console.WriteLine($"Sync error: {args.Exception.Message}");
            // Log to enterprise monitoring system
        };

        var tables = new[] { "Customers", "Orders", "Products", "Inventory", "AuditLogs" };

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await agent.SynchronizeAsync(tables, options);
            stopwatch.Stop();

            Console.WriteLine("Enterprise sync completed successfully!");
            Console.WriteLine($"Duration: {stopwatch.Elapsed.TotalSeconds:F2}s");
            Console.WriteLine($"Total changes: ↑{result.TotalChangesUploaded} ↓{result.TotalChangesDownloaded}");
            Console.WriteLine($"Conflicts resolved: {result.TotalResolvedConflicts}");
            Console.WriteLine($"Throughput: {(result.TotalChangesUploaded + result.TotalChangesDownloaded) / stopwatch.Elapsed.TotalSeconds:F0} changes/sec");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Enterprise sync failed: {ex.Message}");
            // Alert enterprise monitoring system
            throw;
        }
    }

    /// <summary>
    /// Local-first AI agent sync pattern.
    /// </summary>
    public static async Task AIAgentSyncExampleAsync()
    {
        // AI Agent has local SharpCoreDB for offline capability
        var agentDbProvider = new SharpCoreDBSyncProvider("Data Source=ai-agent.db");

        // Cloud database for agent coordination
        var cloudDbProvider = new SqlSyncProvider("Server=cloud;Database=ai-agents;Trusted_Connection=True;");

        var agent = new SyncAgent(agentDbProvider, cloudDbProvider);

        // Agent-specific tables
        var tables = new[] { "Conversations", "Models", "Preferences", "UsageStats" };

        // Tenant filtering for multi-agent environments
        var agentId = "agent-123";
        agent.AddFilter("AgentId", agentId);

        // Sync when online
        if (await IsNetworkAvailableAsync())
        {
            var result = await agent.SynchronizeAsync(tables);
            Console.WriteLine($"AI Agent {agentId} synced: ↑{result.TotalChangesUploaded} ↓{result.TotalChangesDownloaded}");
        }
        else
        {
            Console.WriteLine("AI Agent working offline - no sync performed");
        }
    }

    /// <summary>
    /// IoT device sync pattern with batching.
    /// </summary>
    public static async Task IoTSyncExampleAsync()
    {
        var deviceDbProvider = new SharpCoreDBSyncProvider("Data Source=iot-device.db");
        var cloudDbProvider = new PostgreSqlSyncProvider("Server=iot-cloud;Database=devices;User Id=device;Password=pass;");

        var agent = new SyncAgent(deviceDbProvider, cloudDbProvider);

        // IoT-specific tables
        var tables = new[] { "SensorReadings", "DeviceStatus", "Alerts", "Configurations" };

        // Optimize for IoT: small batches, frequent sync
        var options = new SyncOptions
        {
            BatchSize = 100,  // Small batches for memory-constrained devices
            UseCompression = true,
            UseBulkOperations = false  // Some IoT databases may not support bulk ops
        };

        // Continuous sync loop for IoT devices
        using var cts = new CancellationTokenSource();
        var syncTask = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var result = await agent.SynchronizeAsync(tables, options);
                    Console.WriteLine($"IoT sync: {result.TotalChangesUploaded} readings uploaded");

                    // IoT devices sync frequently
                    await Task.Delay(TimeSpan.FromMinutes(5), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"IoT sync error: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(30), cts.Token); // Retry delay
                }
            }
        }, cts.IsCancellationRequested ? default : CancellationToken.None);

        Console.WriteLine("IoT device sync started. Press Ctrl+C to stop.");
        Console.ReadKey();
        cts.Cancel();
        await syncTask;
    }

    /// <summary>
    /// Helper method to check network availability.
    /// </summary>
    private static async Task<bool> IsNetworkAvailableAsync()
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var response = await client.GetAsync("https://www.google.com");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
