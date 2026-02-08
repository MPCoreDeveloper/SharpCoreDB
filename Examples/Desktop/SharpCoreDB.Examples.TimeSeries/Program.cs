using System;
using System.Threading;
using System.Threading.Tasks;
using SharpCoreDB;
using SharpCoreDB.Examples.TimeSeries;
using SharpCoreDB.Services;
using Microsoft.Extensions.DependencyInjection;

#nullable enable

try
{
    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    Console.WriteLine("SharpCoreDB Time-Series Metrics Collector");
    Console.WriteLine("=========================================\n");

    const string dbPath = "metrics.scdb";

    var services = new ServiceCollection();
    services.AddSharpCoreDB();
    var sp = services.BuildServiceProvider();

    var factory = sp.GetRequiredService<DatabaseFactory>();
    var database = factory.Create(dbPath, "securePassword123");

    await using var collector = new MetricsCollector(database, cts.Token);
    var collectionTask = collector.StartCollectionAsync();

    var monitoringTask = Task.Run(async () =>
    {
        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cts.Token).ConfigureAwait(false);

                var cpuMetrics = collector.QueryMetrics("CPU", TimeSpan.FromHours(1));
                if (cpuMetrics.Count > 0)
                {
                    var avgCpu = 0d;
                    foreach (var m in cpuMetrics)
                        avgCpu += m.Value;
                    avgCpu /= cpuMetrics.Count;
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] CPU Average (1h): {avgCpu:F2}%");
                }

                var memMetrics = collector.QueryMetrics("MemoryUsedMB", TimeSpan.FromHours(1));
                if (memMetrics.Count > 0)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Memory Used: {memMetrics[0].Value:F2} MB");
                }

                var diskMetrics = collector.QueryMetrics("DiskFreeGB", TimeSpan.FromHours(1));
                if (diskMetrics.Count > 0)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Disk Free: {diskMetrics[0].Value:F2} GB\n");
                }

                if (cpuMetrics.Count == 0 && memMetrics.Count == 0 && diskMetrics.Count == 0)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Collecting metrics... (query in 10s)");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    });

    try
    {
        await Task.WhenAll(collectionTask, monitoringTask);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("\nGracefully shutting down...");
    }

    database.Flush();
    database.ForceSave();
    Console.WriteLine("Metrics saved to metrics.scdb");
}
catch (Exception ex)
{
    Console.WriteLine($"\n*** FATAL ERROR ***");
    Console.WriteLine($"{ex.GetType().Name}: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
    if (ex.InnerException is not null)
        Console.WriteLine($"\nInner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
}
