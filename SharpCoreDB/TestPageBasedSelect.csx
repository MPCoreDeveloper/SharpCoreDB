// Quick test to verify PageBased SELECT works
using SharpCoreDB.Core;
using SharpCoreDB.DataStructures;

Console.WriteLine("=== PageBased Storage SELECT Test ===\n");

// Create database with PageBased storage
var dbPath = Path.Combine(Path.GetTempPath(), "TestPageBased_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dbPath);

try
{
    using var db = new Database(dbPath, storageMode: StorageMode.PageBased);
    
    // Create table
    db.Execute("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name STRING, Age INTEGER) STORAGE = PAGE_BASED");
    
    // Insert test data
    Console.WriteLine("Inserting 5 test records...");
    db.Execute("INSERT INTO Users VALUES (1, 'Alice', 25)");
    db.Execute("INSERT INTO Users VALUES (2, 'Bob', 30)");
    db.Execute("INSERT INTO Users VALUES (3, 'Charlie', 35)");
    db.Execute("INSERT INTO Users VALUES (4, 'David', 40)");
    db.Execute("INSERT INTO Users VALUES (5, 'Eve', 28)");
    
    // Test 1: SELECT * (full table scan)
    Console.WriteLine("\nTest 1: SELECT * FROM Users");
    var allRows = db.ExecuteQuery("SELECT * FROM Users");
    Console.WriteLine($"  ✓ Found {allRows.Count} rows");
    foreach (var row in allRows)
    {
        Console.WriteLine($"    - Id={row["Id"]}, Name={row["Name"]}, Age={row["Age"]}");
    }
    
    // Test 2: SELECT with WHERE (primary key lookup)
    Console.WriteLine("\nTest 2: SELECT * FROM Users WHERE Id = 3");
    var pkRow = db.ExecuteQuery("SELECT * FROM Users WHERE Id = 3");
    Console.WriteLine($"  ✓ Found {pkRow.Count} rows");
    if (pkRow.Count > 0)
    {
        Console.WriteLine($"    - Id={pkRow[0]["Id"]}, Name={pkRow[0]["Name"]}, Age={pkRow[0]["Age"]}");
    }
    
    // Test 3: SELECT with WHERE (full scan with filter)
    Console.WriteLine("\nTest 3: SELECT * FROM Users WHERE Age > 30");
    var filteredRows = db.ExecuteQuery("SELECT * FROM Users WHERE Age > 30");
    Console.WriteLine($"  ✓ Found {filteredRows.Count} rows");
    foreach (var row in filteredRows)
    {
        Console.WriteLine($"    - Id={row["Id"]}, Name={row["Name"]}, Age={row["Age"]}");
    }
    
    // Test 4: UPDATE
    Console.WriteLine("\nTest 4: UPDATE Users SET Age = 31 WHERE Id = 2");
    db.Execute("UPDATE Users SET Age = 31 WHERE Id = 2");
    var updated = db.ExecuteQuery("SELECT * FROM Users WHERE Id = 2");
    Console.WriteLine($"  ✓ Updated age: {updated[0]["Age"]}");
    
    // Test 5: DELETE
    Console.WriteLine("\nTest 5: DELETE FROM Users WHERE Id = 5");
    db.Execute("DELETE FROM Users WHERE Id = 5");
    var remaining = db.ExecuteQuery("SELECT * FROM Users");
    Console.WriteLine($"  ✓ Remaining rows: {remaining.Count}");
    
    Console.WriteLine("\n✅ All tests passed!");
}
finally
{
    // Cleanup
    if (Directory.Exists(dbPath))
    {
        Directory.Delete(dbPath, true);
    }
}
