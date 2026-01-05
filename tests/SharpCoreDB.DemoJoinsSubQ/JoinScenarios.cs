using SharpCoreDB;
using System.Diagnostics;

namespace SharpCoreDB.DemoJoinsSubQ;

/// <summary>
/// Demonstrates LEFT/RIGHT/FULL joins and multi-joins with edge cases.
/// Uses public Database API only.
/// </summary>
internal sealed class JoinScenarios
{
    private readonly Interfaces.IDatabase db;

    public JoinScenarios(Interfaces.IDatabase db)
    {
        this.db = db;
    }

    public void RunAll()
    {
        LeftJoin_NoMatches();
        LeftJoin_MultipleMatches();
        RightJoin_Symmetry();
        FullOuter_WithUnmatched();
        MultiJoin_ThreeTables();
    }

    private void PrintResult(string name, List<Dictionary<string, object>> rows, Stopwatch sw)
    {
        Console.WriteLine($"\n{name} -> rows={rows.Count}, time={sw.ElapsedMilliseconds}ms");
        if (rows.Count > 0)
        {
            Console.WriteLine(string.Join(", ", rows[0].Keys));
        }
    }

    private void LeftJoin_NoMatches()
    {
        var sql = @"SELECT c.id, c.name, p.id as payment_id
                     FROM customers c
                     LEFT JOIN payments p ON p.order_id = 99999"; // no matching order_id
        var sw = Stopwatch.StartNew();
        var rows = db.ExecuteQuery(sql);
        sw.Stop();
        // Expect rows == customers count, payment_id NULL
        PrintResult("LEFT JOIN no matches", rows, sw);
    }

    private void LeftJoin_MultipleMatches()
    {
        var sql = @"SELECT o.id as order_id, p.id as payment_id, p.method
                     FROM orders o
                     LEFT JOIN payments p ON p.order_id = o.id
                     WHERE o.id IN (1,2)";
        var sw = Stopwatch.StartNew();
        var rows = db.ExecuteQuery(sql);
        sw.Stop();
        // Expect order 2 to have two payment rows
        PrintResult("LEFT JOIN multiple matches", rows, sw);
    }

    private void RightJoin_Symmetry()
    {
        var sql = @"SELECT p.id as payment_id, o.id as order_id
                     FROM payments p
                     RIGHT JOIN orders o ON p.order_id = o.id
                     WHERE o.id IN (1,2,3)";
        var sw = Stopwatch.StartNew();
        var rows = db.ExecuteQuery(sql);
        sw.Stop();
        // Expect rows for orders even when payment missing
        PrintResult("RIGHT JOIN symmetry", rows, sw);
    }

    private void FullOuter_WithUnmatched()
    {
        var sql = @"SELECT o.id as order_id, p.id as payment_id, p.order_id as pay_order
                     FROM orders o
                     FULL OUTER JOIN payments p ON p.order_id = o.id
                     WHERE o.id IN (1,2,3,4,5) OR p.order_id IN (5,999)";
        var sw = Stopwatch.StartNew();
        var rows = db.ExecuteQuery(sql);
        sw.Stop();
        // Expect unmatched rows from both sides (order 3/4 with no payment, payment 999 with no order)
        PrintResult("FULL OUTER unmatched", rows, sw);
    }

    private void MultiJoin_ThreeTables()
    {
        var sql = @"SELECT c.name, r.name as region, SUM(o.amount) as total_amount
                     FROM customers c
                     LEFT JOIN regions r ON c.region_id = r.id
                     LEFT JOIN orders o ON o.customer_id = c.id
                     GROUP BY c.name, r.name";
        var sw = Stopwatch.StartNew();
        var rows = db.ExecuteQuery(sql);
        sw.Stop();
        // Validate grouping over 3-table join
        PrintResult("Multi-join 3 tables", rows, sw);
    }
}
