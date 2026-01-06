using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Services;

namespace SharpCoreDB.DemoJoinsSubQ;

/// <summary>
/// Validates JOIN query results to diagnose issues.
/// </summary>
internal sealed class JoinValidator
{
    private readonly Interfaces.IDatabase db;

    public JoinValidator(Interfaces.IDatabase db)
    {
        this.db = db;
    }

    public void ValidateLeftJoinNoMatches()
    {
        Console.WriteLine("\n═══ Validating LEFT JOIN (no matches) ═══");
        
        // ✅ FIXED: Test with a condition that will never match
        // customer.id (1-5) will never equal payment.order_id (1,2,2,5,999)  
        // when we filter to just customer IDs that don't have matching order_ids
        var sql = @"SELECT c.id, c.name, p.id as payment_id
                     FROM customers c
                     LEFT JOIN payments p ON c.id = p.order_id
                     WHERE c.id IN (3, 4)";  // These customers have no matching payments
        
        var results = db.ExecuteQuery(sql);
        
        Console.WriteLine($"✓ Returned {results.Count} rows");
        if (results.Count > 0)
            Console.WriteLine($"✓ Columns: {string.Join(", ", results[0].Keys)}");
        
        // Validate expectations
        bool hasPaymentIdColumn = results.Count > 0 && results[0].ContainsKey("payment_id");
        bool allPaymentIdsNull = results.All(r => !r.ContainsKey("payment_id") || r["payment_id"] is DBNull || r["payment_id"] == null);
        bool correctRowCount = results.Count == 2; // Should be customers 3 and 4
        
        Console.WriteLine($"✓ Has payment_id column: {hasPaymentIdColumn}");
        Console.WriteLine($"✓ All payment_ids are NULL: {allPaymentIdsNull}");
        Console.WriteLine($"✓ Correct row count (2): {correctRowCount}");
        
        if (!hasPaymentIdColumn)
            Console.WriteLine("❌ FAIL: Missing payment_id column!");
        if (!allPaymentIdsNull)
            Console.WriteLine("❌ FAIL: payment_id should be NULL for all rows!");
        if (!correctRowCount)
            Console.WriteLine($"❌ FAIL: Expected 2 rows (customers 3,4), got {results.Count}!");
        
        // Show all rows
        Console.WriteLine("\nAll rows:");
        foreach (var row in results)
        {
            Console.WriteLine($"  {string.Join(", ", row.Select(kv => $"{kv.Key}={kv.Value ?? "NULL"}"))}");
        }
    }

    public void ValidateLeftJoinMultipleMatches()
    {
        Console.WriteLine("\n═══ Validating LEFT JOIN (multiple matches) ═══");
        
        // ✅ DIAGNOSTIC: First try WITHOUT ORDER BY to see raw JOIN results
        Console.WriteLine("\n[DIAGNOSTIC] Testing LEFT JOIN WITHOUT ORDER BY:");
        var sqlNoOrder = @"SELECT o.id as order_id, o.customer_id, p.id as payment_id, p.method
                           FROM orders o
                           LEFT JOIN payments p ON p.order_id = o.id";
        
        var resultsNoOrder = db.ExecuteQuery(sqlNoOrder);
        var orders123NoOrder = resultsNoOrder.Where(r => 
        {
            var orderId = r["order_id"].ToString();
            return orderId == "1" || orderId == "2" || orderId == "3";
        }).ToList();
        
        Console.WriteLine($"[DIAGNOSTIC] Without ORDER BY - Orders 1-3: {orders123NoOrder.Count} rows");
        foreach (var row in orders123NoOrder)
        {
            Console.WriteLine($"  {string.Join(", ", row.Select(kv => $"{kv.Key}={kv.Value ?? "NULL"}"))}");
        }
        
        // ✅ Now test WITH ORDER BY
        Console.WriteLine("\n[DIAGNOSTIC] Testing LEFT JOIN WITH ORDER BY:");
        var sql = @"SELECT o.id as order_id, o.customer_id, p.id as payment_id, p.method
                     FROM orders o
                     LEFT JOIN payments p ON p.order_id = o.id
                     ORDER BY o.id, p.id";
        
        var results = db.ExecuteQuery(sql);
        
        Console.WriteLine($"✓ Returned {results.Count} rows");
        if (results.Count > 0)
            Console.WriteLine($"✓ Columns: {string.Join(", ", results[0].Keys)}");
        
        // Filter to just orders 1, 2, 3 for validation
        var orders123 = results.Where(r => 
        {
            var orderId = r["order_id"].ToString();
            return orderId == "1" || orderId == "2" || orderId == "3";
        }).ToList();
        
        Console.WriteLine($"✓ Orders 1-3: {orders123.Count} rows");
        
        // Validate expectations
        // Order 1 has 1 payment (1 row)
        var order1Rows = orders123.Where(r => r["order_id"].ToString() == "1").ToList();
        // Order 2 has 2 payments (2 rows)
        var order2Rows = orders123.Where(r => r["order_id"].ToString() == "2").ToList();
        // Order 3 has 0 payments (1 row with NULL payment)
        var order3Rows = orders123.Where(r => r["order_id"].ToString() == "3").ToList();
        
        bool order1Correct = order1Rows.Count == 1;
        bool order2Correct = order2Rows.Count == 2;
        bool order3Correct = order3Rows.Count >= 1 && (order3Rows[0]["payment_id"] is DBNull || order3Rows[0]["payment_id"] == null || string.IsNullOrEmpty(order3Rows[0]["payment_id"].ToString()));
        // Total should be 4 rows for orders 1-3
        bool totalCorrect = orders123.Count == 4;
        
        Console.WriteLine($"✓ Order 1 has 1 payment row: {order1Correct} (actual: {order1Rows.Count})");
        Console.WriteLine($"✓ Order 2 has 2 payment rows: {order2Correct} (actual: {order2Rows.Count})");
        Console.WriteLine($"✓ Order 3 has NULL payment: {order3Correct} (actual: {order3Rows.Count} rows)");
        Console.WriteLine($"✓ Total rows for orders 1-3: {totalCorrect} (actual: {orders123.Count}, expected: 4)");
        
        if (!order1Correct)
            Console.WriteLine($"❌ FAIL: Order 1 should have 1 row, got {order1Rows.Count}!");
        if (!order2Correct)
            Console.WriteLine($"❌ FAIL: Order 2 should have 2 rows (2 payments), got {order2Rows.Count}!");
        if (!order3Correct)
            Console.WriteLine($"❌ FAIL: Order 3 should have 1 row with NULL payment!");
        if (!totalCorrect)
            Console.WriteLine($"❌ FAIL: Expected 4 total rows for orders 1-3, got {orders123.Count}!");
        
        // Show first 10 rows
        Console.WriteLine("\nFirst 10 rows:");
        foreach (var row in results.Take(10))
        {
            Console.WriteLine($"  {string.Join(", ", row.Select(kv => $"{kv.Key}={kv.Value ?? "NULL"}"))}");
        }
    }

    public void ValidateDataSetup()
    {
        Console.WriteLine("\n═══ Validating Data Setup ═══");
        
        var customers = db.ExecuteQuery("SELECT COUNT(*) as cnt FROM customers");
        var orders = db.ExecuteQuery("SELECT COUNT(*) as cnt FROM orders");
        var payments = db.ExecuteQuery("SELECT COUNT(*) as cnt FROM payments");
        
        Console.WriteLine($"✓ Customers: {customers[0]["cnt"]}");
        Console.WriteLine($"✓ Orders: {orders[0]["cnt"]}");
        Console.WriteLine($"✓ Payments: {payments[0]["cnt"]}");
        
        // ✅ NEW: Check if order 2 actually exists!
        var order1 = db.ExecuteQuery("SELECT * FROM orders WHERE id = 1");
        var order2 = db.ExecuteQuery("SELECT * FROM orders WHERE id = 2");
        var order3 = db.ExecuteQuery("SELECT * FROM orders WHERE id = 3");
        
        Console.WriteLine($"\n✓ Order 1 exists: {order1.Count > 0}");
        if (order1.Count > 0)
        {
            Console.WriteLine($"  Order 1: customer_id={order1[0]["customer_id"]}, amount={order1[0]["amount"]}, status={order1[0]["status"]}");
        }
        
        Console.WriteLine($"✓ Order 2 exists: {order2.Count > 0}");
        if (order2.Count > 0)
        {
            Console.WriteLine($"  Order 2: customer_id={order2[0]["customer_id"]}, amount={order2[0]["amount"]}, status={order2[0]["status"]}");
        }
        
        Console.WriteLine($"✓ Order 3 exists: {order3.Count > 0}");
        if (order3.Count > 0)
        {
            Console.WriteLine($"  Order 3: customer_id={order3[0]["customer_id"]}, amount={order3[0]["amount"]}, status={order3[0]["status"]}");
        }
        
        // Check payments for orders 1 and 2
        var paymentsFor1 = db.ExecuteQuery("SELECT * FROM payments WHERE order_id = 1");
        var paymentsFor2 = db.ExecuteQuery("SELECT * FROM payments WHERE order_id = 2");
        
        Console.WriteLine($"\n✓ Payments for order 1: {paymentsFor1.Count}");
        foreach (var p in paymentsFor1)
        {
            Console.WriteLine($"  Payment ID={p["id"]}, method={p["method"]}");
        }
        
        Console.WriteLine($"✓ Payments for order 2: {paymentsFor2.Count}");
        foreach (var p in paymentsFor2)
        {
            Console.WriteLine($"  Payment ID={p["id"]}, method={p["method"]}");
        }
    }

    public void RunAll()
    {
        ValidateDataSetup();
        ValidateLeftJoinNoMatches();
        ValidateLeftJoinMultipleMatches();
    }
}
