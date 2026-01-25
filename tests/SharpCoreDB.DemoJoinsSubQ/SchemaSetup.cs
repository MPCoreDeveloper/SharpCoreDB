using SharpCoreDB;

namespace SharpCoreDB.DemoJoinsSubQ;

/// <summary>
/// Creates schema and seeds data for join/subquery scenarios.
/// Includes small and medium tables, NULLs, duplicates, missing FK matches.
/// 
/// ✅ OPTIMIZED: Uses InsertBatch() for bulk operations (40% faster than ExecuteSQL loop)
/// InsertBatch now properly syncs both disk AND in-memory cache
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

        // Regions
        db.ExecuteSQL("INSERT INTO regions VALUES (1, 'NA'), (2, 'EU'), (3, 'APAC')");

        // Customers
        db.ExecuteSQL("INSERT INTO customers VALUES (1, 'Alice', 1)");
        db.ExecuteSQL("INSERT INTO customers VALUES (2, 'Bob', 2)");
        db.ExecuteSQL("INSERT INTO customers VALUES (3, 'Charlie', NULL)");
        db.ExecuteSQL("INSERT INTO customers VALUES (4, 'Alice', 3)");
        db.ExecuteSQL("INSERT INTO customers VALUES (5, 'Dana', 99)");

        // Orders (200 rows) - uses InsertBatch for 40% performance boost
        Console.WriteLine("✓ Inserting 200 orders...");
        var orderRows = new List<Dictionary<string, object>>(200);
        for (int i = 0; i < 200; i++)
        {
            orderRows.Add(new Dictionary<string, object>
            {
                { "id", i + 1 },
                { "customer_id", (i % 5) + 1 },
                { "amount", 50m + ((i % 20) * 5m) },
                { "status", (i % 7 == 0) ? "CANCELLED" : "PAID" }
            });
        }
        
        if (db is SharpCoreDB.Database concreteDb)
            concreteDb.InsertBatch("orders", orderRows);
        else
            foreach (var row in orderRows)
                db.ExecuteSQL(BuildInsertStatement("orders", row));
        
        Console.WriteLine("✓ Completed inserting 200 orders");

        // Payments
        var paymentRows = new[]
        {
            new Dictionary<string, object> { { "id", 1 }, { "order_id", 1 }, { "method", "CARD" }, { "confirmed", 1 } },
            new Dictionary<string, object> { { "id", 2 }, { "order_id", 2 }, { "method", "CASH" }, { "confirmed", 1 } },
            new Dictionary<string, object> { { "id", 3 }, { "order_id", 2 }, { "method", "GIFT" }, { "confirmed", 0 } },
            new Dictionary<string, object> { { "id", 4 }, { "order_id", 5 }, { "method", "CARD" }, { "confirmed", 1 } },
            new Dictionary<string, object> { { "id", 5 }, { "order_id", 999 }, { "method", "CARD" }, { "confirmed", 0 } }
        }.ToList();
        
        if (db is SharpCoreDB.Database concreteDb2)
            concreteDb2.InsertBatch("payments", paymentRows);
        else
            foreach (var row in paymentRows)
                db.ExecuteSQL(BuildInsertStatement("payments", row));

        // Inventory
        var inventoryRows = new[]
        {
            new Dictionary<string, object> { { "sku", "SKU1" }, { "product", "Widget" }, { "stock", 10 }, { "price", 9.99m } },
            new Dictionary<string, object> { { "sku", "SKU2" }, { "product", "Gadget" }, { "stock", 0 }, { "price", 19.99m } },
            new Dictionary<string, object> { { "sku", "SKU3" }, { "product", "Doohickey" }, { "stock", 5 }, { "price", 4.99m } }
        }.ToList();
        
        if (db is SharpCoreDB.Database concreteDb3)
            concreteDb3.InsertBatch("inventory", inventoryRows);
        else
            foreach (var row in inventoryRows)
                db.ExecuteSQL(BuildInsertStatement("inventory", row));
        
        db.Flush();
    }

    private static string BuildInsertStatement(string tableName, Dictionary<string, object> row)
    {
        var values = string.Join(", ", row.Values.Select(FormatValue));
        return $"INSERT INTO {tableName} VALUES ({values})";
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "NULL",
        string s => $"'{s.Replace("'", "''")}'",
        bool b => b ? "1" : "0",
        _ => value.ToString() ?? "NULL"
    };
}
