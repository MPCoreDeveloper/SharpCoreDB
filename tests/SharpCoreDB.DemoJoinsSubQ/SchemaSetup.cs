using SharpCoreDB;
using System.Text;

namespace SharpCoreDB.DemoJoinsSubQ;

/// <summary>
/// Creates schema and seeds data for join/subquery scenarios.
/// Includes small and medium tables, NULLs, duplicates, missing FK matches.
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

        // Orders (medium-ish ~200 for demo; can be scaled)
        Console.WriteLine("✓ Inserting 200 orders...");
        for (int i = 0; i < 200; i++)
        {
            int orderId = i + 1;
            int custId = (i % 5) + 1; // distribute over 5 customers
            decimal amount = 50 + (i % 20) * 5;
            string status = (i % 7 == 0) ? "CANCELLED" : "PAID";
            
            db.ExecuteSQL($"INSERT INTO orders VALUES ({orderId}, {custId}, {amount}, '{status}')");
        }
        Console.WriteLine("✓ Completed inserting 200 orders");

        // Payments (some missing, some duplicates)
        db.ExecuteSQL("INSERT INTO payments VALUES (1, 1, 'CARD', 1)");
        db.ExecuteSQL("INSERT INTO payments VALUES (2, 2, 'CASH', 1)");
        db.ExecuteSQL("INSERT INTO payments VALUES (3, 2, 'GIFT', 0)"); // duplicate order with different method
        db.ExecuteSQL("INSERT INTO payments VALUES (4, 5, 'CARD', 1)");
        db.ExecuteSQL("INSERT INTO payments VALUES (5, 999, 'CARD', 0)"); // missing order

        // Inventory (table for subqueries)
        db.ExecuteSQL("INSERT INTO inventory VALUES ('SKU1', 'Widget', 10, 9.99)");
        db.ExecuteSQL("INSERT INTO inventory VALUES ('SKU2', 'Gadget', 0, 19.99)");
        db.ExecuteSQL("INSERT INTO inventory VALUES ('SKU3', 'Doohickey', 5, 4.99)");
    }
}
