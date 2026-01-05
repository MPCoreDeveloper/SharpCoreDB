using SharpCoreDB;
using System.Diagnostics;

namespace SharpCoreDB.DemoJoinsSubQ;

/// <summary>
/// Demonstrates scalar, correlated, FROM, and HAVING subqueries plus reuse/cache.
/// </summary>
internal sealed class SubqueryScenarios
{
    private readonly Interfaces.IDatabase db;

    public SubqueryScenarios(Interfaces.IDatabase db)
    {
        this.db = db;
    }

    public void RunAll()
    {
        ScalarSubquery_SelectList();
        NonCorrelated_In_From();
        Correlated_Subquery_Where();
        Having_With_Subquery();
        Subquery_Reuse_Timing();
    }

    private void PrintResult(string name, List<Dictionary<string, object>> rows, Stopwatch sw)
    {
        Console.WriteLine($"\n{name} -> rows={rows.Count}, time={sw.ElapsedMilliseconds}ms");
        if (rows.Count > 0)
        {
            Console.WriteLine(string.Join(", ", rows[0].Keys));
        }
    }

    private void ScalarSubquery_SelectList()
    {
        var sql = @"SELECT c.name,
                            (SELECT MAX(amount) FROM orders) AS max_order_amount
                     FROM customers c
                     WHERE c.id IN (1,2)";
        var sw = Stopwatch.StartNew();
        var rows = db.ExecuteQuery(sql);
        sw.Stop();
        // Expect same max value repeated per row
        PrintResult("Scalar subquery in SELECT", rows, sw);
    }

    private void NonCorrelated_In_From()
    {
        var sql = @"SELECT x.region, x.total_amount
                     FROM (
                        SELECT r.name as region, SUM(o.amount) as total_amount
                        FROM regions r
                        LEFT JOIN customers c ON c.region_id = r.id
                        LEFT JOIN orders o ON o.customer_id = c.id
                        GROUP BY r.name
                     ) x
                     WHERE x.total_amount IS NOT NULL";
        var sw = Stopwatch.StartNew();
        var rows = db.ExecuteQuery(sql);
        sw.Stop();
        PrintResult("Derived table in FROM", rows, sw);
    }

    private void Correlated_Subquery_Where()
    {
        var sql = @"SELECT c.name
                     FROM customers c
                     WHERE EXISTS (
                        SELECT 1 FROM orders o
                        WHERE o.customer_id = c.id AND o.amount > 100
                     )";
        var sw = Stopwatch.StartNew();
        var rows = db.ExecuteQuery(sql);
        sw.Stop();
        PrintResult("Correlated EXISTS", rows, sw);
    }

    private void Having_With_Subquery()
    {
        var sql = @"SELECT c.region_id, SUM(o.amount) as total_amount
                     FROM customers c
                     LEFT JOIN orders o ON o.customer_id = c.id
                     GROUP BY c.region_id
                     HAVING SUM(o.amount) > (
                        SELECT AVG(amount) FROM orders
                     )";
        var sw = Stopwatch.StartNew();
        var rows = db.ExecuteQuery(sql);
        sw.Stop();
        PrintResult("HAVING with scalar subquery", rows, sw);
    }

    private void Subquery_Reuse_Timing()
    {
        var sql = @"SELECT c.name,
                            (SELECT AVG(amount) FROM orders) as avg_all,
                            (SELECT AVG(amount) FROM orders) as avg_all_2
                     FROM customers c
                     WHERE c.id <= 3";

        // Expect optimizer/cache to execute non-correlated AVG once
        var sw = Stopwatch.StartNew();
        var rows = db.ExecuteQuery(sql);
        sw.Stop();
        PrintResult("Subquery reuse (same subquery twice)", rows, sw);
    }
}
