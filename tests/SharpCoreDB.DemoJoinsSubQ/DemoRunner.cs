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
