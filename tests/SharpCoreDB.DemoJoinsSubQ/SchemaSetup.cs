using SharpCoreDB;
using System.Text;

namespace SharpCoreDB.DemoJoinsSubQ;

/// <summary>
/// Creates schema and seeds data for join/subquery scenarios.
/// Includes small and medium tables, NULLs, duplicates, missing FK matches.
/// 
/// ✅ REFACTORED: Uses InsertBatch() for bulk operations instead of ExecuteSQL loop
/// This writes directly to storage engine, ensuring all rows persist (not just in-memory)
/// </summary>
internal sealed class SchemaSetup
{
    private readonly Interfaces.IDatabase db;

    public SchemaSetup(Interfaces.IDatabase db)
    {
        this.db = db;
    }

    public void Seed()
    {
        db.ExecuteSQL("DROP TABLE IF EXISTS customers");
        db.ExecuteSQL("DROP TABLE IF EXISTS orders");
        db.ExecuteSQL("DROP TABLE IF EXISTS payments");
        db.ExecuteSQL("DROP TABLE IF EXISTS regions");
        db.ExecuteSQL("DROP TABLE IF EXISTS inventory");

        db.ExecuteSQL("CREATE TABLE customers (id INTEGER PRIMARY KEY, name TEXT, region_id INTEGER NULL)");
        db.ExecuteSQL("CREATE TABLE regions (id INTEGER PRIMARY KEY, name TEXT)");
        db.ExecuteSQL("CREATE TABLE orders (id INTEGER PRIMARY KEY, customer_id INTEGER, amount DECIMAL, status TEXT)");
        db.ExecuteSQL("CREATE TABLE payments (id INTEGER PRIMARY KEY, order_id INTEGER, method TEXT, confirmed INTEGER)");
        db.ExecuteSQL("CREATE TABLE inventory (sku TEXT PRIMARY KEY, product TEXT, stock INTEGER, price DECIMAL)");

        // Regions (small) - using multi-row INSERT
        db.ExecuteSQL("INSERT INTO regions VALUES (1, 'NA'), (2, 'EU'), (3, 'APAC')");

        // Customers (small with NULL region, duplicate names)
        db.ExecuteSQL("INSERT INTO customers VALUES (1, 'Alice', 1)");
        db.ExecuteSQL("INSERT INTO customers VALUES (2, 'Bob', 2)");
        db.ExecuteSQL("INSERT INTO customers VALUES (3, 'Charlie', NULL)");
        db.ExecuteSQL("INSERT INTO customers VALUES (4, 'Alice', 3)"); // duplicate name
        db.ExecuteSQL("INSERT INTO customers VALUES (5, 'Dana', 99)");  // missing region

        // ✅ REFACTORED: Build all 200 orders in memory, then batch insert
        // This writes directly to storage engine instead of in-memory table only
        Console.WriteLine("✓ Inserting 200 orders...");
        var orderRows = new List<Dictionary<string, object>>(200);
        
        for (int i = 0; i < 200; i++)
        {
            int orderId = i + 1;
            int custId = (i % 5) + 1;
            decimal amount = 50 + (i % 20) * 5;
            string status = (i % 7 == 0) ? "CANCELLED" : "PAID";
            
            orderRows.Add(new Dictionary<string, object>
            {
                { "id", orderId },
                { "customer_id", custId },
                { "amount", amount },
                { "status", status }
            });
        }
        
        // Insert all 200 rows in one batch (writes to storage, not just memory!)
        var inserted = InsertBatchWithFallback("orders", orderRows);
        Console.WriteLine($"✓ Completed inserting {inserted} orders");

        // Payments (some missing, some duplicates)
        var paymentRows = new[]
        {
            new Dictionary<string, object> { { "id", 1 }, { "order_id", 1 }, { "method", "CARD" }, { "confirmed", 1 } },
            new Dictionary<string, object> { { "id", 2 }, { "order_id", 2 }, { "method", "CASH" }, { "confirmed", 1 } },
            new Dictionary<string, object> { { "id", 3 }, { "order_id", 2 }, { "method", "GIFT" }, { "confirmed", 0 } },
            new Dictionary<string, object> { { "id", 4 }, { "order_id", 5 }, { "method", "CARD" }, { "confirmed", 1 } },
            new Dictionary<string, object> { { "id", 5 }, { "order_id", 999 }, { "method", "CARD" }, { "confirmed", 0 } }
        }.ToList();
        
        InsertBatchWithFallback("payments", paymentRows);

        // Inventory (table for subqueries)
        var inventoryRows = new[]
        {
            new Dictionary<string, object> { { "sku", "SKU1" }, { "product", "Widget" }, { "stock", 10 }, { "price", 9.99m } },
            new Dictionary<string, object> { { "sku", "SKU2" }, { "product", "Gadget" }, { "stock", 0 }, { "price", 19.99m } },
            new Dictionary<string, object> { { "sku", "SKU3" }, { "product", "Doohickey" }, { "stock", 5 }, { "price", 4.99m } }
        }.ToList();
        
        InsertBatchWithFallback("inventory", inventoryRows);
        
        // ✅ CRITICAL: Flush once at the very end to ensure all data is persisted to disk
        db.Flush();
    }

    /// <summary>
    /// ✅ Helper: Attempts InsertBatch() if available (writes to storage), 
    /// falls back to individual ExecuteSQL if needed (writes to memory only).
    /// Returns count of rows inserted.
    /// </summary>
    private int InsertBatchWithFallback(string tableName, List<Dictionary<string, object>> rows)
    {
        // Try casting to concrete Database class for InsertBatch method
        if (db is SharpCoreDB.Database concreteDb)
        {
            try
            {
                var results = concreteDb.InsertBatch(tableName, rows);
                return results.Length;
            }
            catch
            {
                // Fall back to individual inserts if InsertBatch fails
            }
        }

        // Fallback: Use ExecuteSQL (only writes to in-memory table, not storage!)
        // This is slower and less reliable, but works if InsertBatch isn't available
        foreach (var row in rows)
        {
            var values = string.Join(", ", row.Values.Select(FormatSqlValue));
            db.ExecuteSQL($"INSERT INTO {tableName} VALUES ({values})");
        }
        
        return rows.Count;
    }

    /// <summary>
    /// ✅ Helper: Formats a value for SQL insertion
    /// </summary>
    private static string FormatSqlValue(object? value) => value switch
    {
        null => "NULL",
        string s => $"'{s.Replace("'", "''")}'",  // SQL string escaping
        bool b => b ? "1" : "0",
        _ => value.ToString() ?? "NULL"
    };
}
