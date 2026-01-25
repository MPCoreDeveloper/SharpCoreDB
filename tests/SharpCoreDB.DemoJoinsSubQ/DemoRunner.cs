using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Services;
using System.Diagnostics;

namespace SharpCoreDB.DemoJoinsSubQ;

internal sealed class DemoRunner
{
    private readonly DatabaseFactory factory;

    public DemoRunner()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        factory = provider.GetRequiredService<DatabaseFactory>();
    }

    public void Run()
    {
        var path = Path.Combine(Path.GetTempPath(), $"joins_subq_{Guid.NewGuid():N}.db");
        var db = factory.Create(path, "demo-pass");

        try
        {
            var setup = new SchemaSetup(db);
            setup.Seed();

            // ✅ CRITICAL: Flush all pending inserts to disk BEFORE querying
            // Otherwise WAL buffer has uncommitted data and queries see stale state
            db.Flush();

            // Before the validation SELECT
            Console.WriteLine("\n[DEBUG] BEFORE VALIDATION SELECT:");
            var ordersTable = db.ExecuteQuery("SELECT COUNT(*) as cnt FROM orders");
            Console.WriteLine($"[DEBUG] Row count from SELECT: {ordersTable[0]["cnt"]}");

            // Try to access the table directly to see in-memory count
            if (db is Database concreteDb)
            {
                var allOrders = concreteDb.ExecuteQuery("SELECT id FROM orders");
                Console.WriteLine($"[DEBUG] Direct table count: {allOrders.Count}");

                // Check last few order IDs
                if (allOrders.Count > 0)
                {
                    Console.WriteLine($"[DEBUG] First order ID: {allOrders[0]["id"]}");
                    Console.WriteLine($"[DEBUG] Last order ID: {allOrders[allOrders.Count - 1]["id"]}");

                    // Check for gaps around the 100-141 boundary
                    var ids = allOrders.Select(r => Convert.ToInt32(r["id"])).OrderBy(x => x).ToList();
                    var missing = new List<int>();
                    for (int i = 1; i <= 200; i++)
                    {
                        if (!ids.Contains(i))
                            missing.Add(i);
                    }
                    Console.WriteLine($"[DEBUG] Missing order IDs: {string.Join(",", missing.Take(20))}...");
                }
            }

            // ✅ Run validation first to diagnose issues
            var validator = new JoinValidator(db);
            validator.RunAll();

            // Run full demo scenarios
            var joins = new JoinScenarios(db);
            var subq = new SubqueryScenarios(db);
            joins.RunAll();
            subq.RunAll();

            Console.WriteLine("Demo complete.");
        }
        finally
        {
            try { File.Delete(path); } catch { }
            (db as IDisposable)?.Dispose();
        }
    }
}
