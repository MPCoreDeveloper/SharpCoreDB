# StructRow API Usage Examples

This document provides comprehensive examples for using SharpCoreDB's StructRow API - the zero-copy, high-performance query interface.

## Table of Contents

1. [Basic Usage](#basic-usage)
2. [Advanced Features](#advanced-features)
3. [Performance Optimization](#performance-optimization)
4. [Migration Guide](#migration-guide)
5. [Error Handling](#error-handling)
6. [Real-World Examples](#real-world-examples)

## Basic Usage

### Simple Iteration

```csharp
using SharpCoreDB.DataStructures;

// Create database connection
using var db = factory.Create("./myapp.db", "password");

// Traditional Dictionary API (slower)
var dictResults = db.Select("SELECT id, name, age FROM users");
foreach (var row in dictResults)
{
    int id = (int)row["id"];
    string name = (string)row["name"];
    int age = (int)row["age"];
    Console.WriteLine($"{id}: {name} ({age})");
}

// New StructRow API (zero-copy, faster)
var structResults = db.SelectStruct("SELECT id, name, age FROM users");
foreach (var row in structResults)
{
    int id = row.GetValue<int>(0);        // By column index
    string name = row.GetValue<string>(1);
    int age = row.GetValue<int>(2);
    Console.WriteLine($"{id}: {name} ({age})");
}
```

### Column Access by Name

```csharp
var results = db.SelectStruct("SELECT * FROM products");
foreach (var row in results)
{
    // Access by column name (slightly slower than index)
    string name = row.GetValue<string>("name");
    decimal price = row.GetValue<decimal>("price");
    bool active = row.GetValue<bool>("active");
    Guid productId = row.GetValue<Guid>("id");

    if (active)
    {
        Console.WriteLine($"{name}: ${price} (ID: {productId})");
    }
}
```

### Null Handling

```csharp
var results = db.SelectStruct("SELECT name, email, phone FROM contacts");
foreach (var row in results)
{
    string name = row.GetValue<string>("name");

    // Safe null checking
    if (!row.IsNull("email"))
    {
        string email = row.GetValue<string>("email");
        Console.WriteLine($"{name} <{email}>");
    }
    else
    {
        Console.WriteLine($"{name} (no email)");
    }

    // Handle nullable phone
    string phone = row.IsNull("phone") ? "N/A" : row.GetValue<string>("phone");
    Console.WriteLine($"Phone: {phone}");
}
```

## Advanced Features

### Caching for Repeated Access

```csharp
// Enable caching when accessing the same columns multiple times
var results = db.SelectStruct("SELECT * FROM users", enableCaching: true);
foreach (var row in results)
{
    // First access deserializes and caches
    string firstName = row.GetValue<string>("first_name");
    string lastName = row.GetValue<string>("last_name");

    // Subsequent accesses use cached values (much faster)
    string displayName = $"{firstName} {lastName}";
    string searchKey = $"{lastName}, {firstName}";
    string emailKey = $"{firstName}.{lastName}@company.com";

    ProcessUser(displayName, searchKey, emailKey);
}
```

### Parallel Processing

```csharp
// Use parallel processing for large datasets
var results = db.SelectStructParallel("SELECT id, data FROM large_table");

// Process in parallel (be careful with shared state)
Parallel.ForEach(results, row =>
{
    int id = row.GetValue<int>(0);
    byte[] data = row.GetValue<byte[]>(1);

    // Process data in parallel
    ProcessLargeData(id, data);
});
```

### Working with Different Data Types

```csharp
var results = db.SelectStruct("SELECT * FROM sensor_data");
foreach (var row in results)
{
    // All supported data types
    Guid sensorId = row.GetValue<Guid>("sensor_id");
    string sensorName = row.GetValue<string>("name");
    double temperature = row.GetValue<double>("temperature");
    int humidity = row.GetValue<int>("humidity");
    bool active = row.GetValue<bool>("active");
    DateTime timestamp = row.GetValue<DateTime>("timestamp");
    decimal batteryLevel = row.GetValue<decimal>("battery_level");
    Ulid readingId = row.GetValue<Ulid>("reading_id");
    byte[] rawData = row.GetValue<byte[]>("raw_data");

    // Process sensor reading
    ProcessSensorReading(sensorId, temperature, humidity, timestamp);
}
```

## Performance Optimization

### Choose the Right Access Pattern

```csharp
// ✅ FASTEST: Column index access
foreach (var row in db.SelectStruct("SELECT id, name, age FROM users"))
{
    int id = row.GetValue<int>(0);        // Direct offset access
    string name = row.GetValue<string>(1); // Direct offset access
    int age = row.GetValue<int>(2);       // Direct offset access
}

// ⚠️ SLOWER: Column name access (string lookup overhead)
foreach (var row in db.SelectStruct("SELECT id, name, age FROM users"))
{
    int id = row.GetValue<int>("id");        // String dictionary lookup
    string name = row.GetValue<string>("name");
    int age = row.GetValue<int>("age");
}

// ✅ GOOD: Mix when needed, cache indices
var results = db.SelectStruct("SELECT * FROM users");
int idIndex = 0;  // Cache column indices
int nameIndex = 1;
int ageIndex = 2;

foreach (var row in results)
{
    int id = row.GetValue<int>(idIndex);
    string name = row.GetValue<string>(nameIndex);
    int age = row.GetValue<int>(ageIndex);
}
```

### Memory Management

```csharp
// ✅ CORRECT: Process results within the query scope
void ProcessUsers()
{
    var results = db.SelectStruct("SELECT * FROM users");
    foreach (var row in results)
    {
        // StructRow is only valid within this scope
        ProcessUser(row);
    }
    // Results disposed here, StructRow instances invalidated
}

// ❌ WRONG: Don't store StructRow instances
List<StructRow> storedRows = new();
var results = db.SelectStruct("SELECT * FROM users");
foreach (var row in results)
{
    storedRows.Add(row);  // ❌ Data becomes invalid after query
}
// storedRows now contains invalid data!
```

### Batch Processing

```csharp
// Process in batches to control memory usage
const int batchSize = 1000;
int offset = 0;

while (true)
{
    var batch = db.SelectStruct($"SELECT * FROM large_table LIMIT {batchSize} OFFSET {offset}");
    int count = 0;

    foreach (var row in batch)
    {
        ProcessRow(row);
        count++;
    }

    if (count < batchSize) break;  // No more data
    offset += batchSize;
}
```

## Migration Guide

### From Dictionary API

#### Before (Dictionary API)
```csharp
public List<User> GetUsers(Database db)
{
    var results = db.Select("SELECT id, name, email, age FROM users");
    var users = new List<User>();

    foreach (var row in results)
    {
        var user = new User
        {
            Id = (int)row["id"],
            Name = (string)row["name"],
            Email = (string)row["email"],
            Age = (int)row["age"]
        };
        users.Add(user);
    }

    return users;
}
```

#### After (StructRow API)
```csharp
public List<User> GetUsers(Database db)
{
    var results = db.SelectStruct("SELECT id, name, email, age FROM users");
    var users = new List<User>();

    foreach (var row in results)
    {
        var user = new User
        {
            Id = row.GetValue<int>(0),
            Name = row.GetValue<string>(1),
            Email = row.GetValue<string>(2),
            Age = row.GetValue<int>(3)
        };
        users.Add(user);
    }

    return users;
}
```

### LINQ-Style Processing

```csharp
// Before: Manual filtering
var adults = new List<User>();
var results = db.Select("SELECT * FROM users");
foreach (var row in results)
{
    int age = (int)row["age"];
    if (age >= 18)
    {
        adults.Add(new User { /* ... */ });
    }
}

// After: Lazy evaluation with StructRow
var adults = db.SelectStruct("SELECT * FROM users")
    .Where(row => row.GetValue<int>("age") >= 18)
    .Select(row => new User
    {
        Id = row.GetValue<int>("id"),
        Name = row.GetValue<string>("name"),
        Age = row.GetValue<int>("age")
    })
    .ToList();
```

## Error Handling

### Type Safety at Compile Time

```csharp
// ✅ Compile-time safety prevents invalid casts
foreach (var row in db.SelectStruct("SELECT id, name FROM users"))
{
    int id = row.GetValue<int>(0);        // ✅ Correct type
    string name = row.GetValue<string>(1); // ✅ Correct type

    // Compiler error - can't assign int to string
    // string invalid = row.GetValue<int>(0); // ❌ Compile error
}
```

### Runtime Error Handling

```csharp
foreach (var row in db.SelectStruct("SELECT * FROM users"))
{
    try
    {
        int id = row.GetValue<int>("id");
        string name = row.GetValue<string>("name");

        // Safe null handling
        string email = row.IsNull("email") ? null : row.GetValue<string>("email");

        ProcessUser(id, name, email);
    }
    catch (InvalidCastException ex)
    {
        // Handle type mismatches (should be rare with proper schema)
        Log.Error($"Type mismatch in row: {ex.Message}");
    }
    catch (ArgumentException ex)
    {
        // Handle missing columns
        Log.Error($"Column not found: {ex.Message}");
    }
    catch (ArgumentOutOfRangeException ex)
    {
        // Handle invalid column indices
        Log.Error($"Invalid column index: {ex.Message}");
    }
}
```

### Defensive Programming

```csharp
public User? GetUserById(Database db, int userId)
{
    try
    {
        var results = db.SelectStruct($"SELECT * FROM users WHERE id = {userId}");

        foreach (var row in results)
        {
            // Validate all required fields exist and are correct type
            if (row.IsNull("id") || row.IsNull("name")) continue;

            return new User
            {
                Id = row.GetValue<int>("id"),
                Name = row.GetValue<string>("name"),
                Email = row.IsNull("email") ? null : row.GetValue<string>("email"),
                Age = row.IsNull("age") ? 0 : row.GetValue<int>("age")
            };
        }
    }
    catch (Exception ex)
    {
        Log.Error($"Error retrieving user {userId}: {ex.Message}");
    }

    return null;
}
```

## Real-World Examples

### E-commerce Product Catalog

```csharp
public class ProductService
{
    private readonly Database _db;

    public ProductService(Database db)
    {
        _db = db;
    }

    public IEnumerable<Product> GetActiveProducts()
    {
        var results = _db.SelectStruct("SELECT * FROM products WHERE active = 1");

        foreach (var row in results)
        {
            yield return new Product
            {
                Id = row.GetValue<Guid>("id"),
                Name = row.GetValue<string>("name"),
                Price = row.GetValue<decimal>("price"),
                Category = row.GetValue<string>("category"),
                InStock = row.GetValue<int>("stock_quantity") > 0,
                LastUpdated = row.GetValue<DateTime>("last_updated")
            };
        }
    }

    public decimal CalculateTotalValue()
    {
        decimal total = 0;
        var results = _db.SelectStruct("SELECT price, stock_quantity FROM products WHERE active = 1");

        foreach (var row in results)
        {
            decimal price = row.GetValue<decimal>(0);
            int quantity = row.GetValue<int>(1);
            total += price * quantity;
        }

        return total;
    }
}
```

### IoT Sensor Data Processing

```csharp
public class SensorDataProcessor
{
    private readonly Database _db;

    public void ProcessRecentReadings()
    {
        // Get readings from last hour
        var cutoff = DateTime.UtcNow.AddHours(-1);
        var results = _db.SelectStruct($"SELECT * FROM sensor_readings WHERE timestamp > '{cutoff:yyyy-MM-dd HH:mm:ss}'");

        var readingsBySensor = new Dictionary<Guid, List<SensorReading>>();

        foreach (var row in results)
        {
            var reading = new SensorReading
            {
                SensorId = row.GetValue<Guid>("sensor_id"),
                Temperature = row.GetValue<double>("temperature"),
                Humidity = row.GetValue<int>("humidity"),
                Timestamp = row.GetValue<DateTime>("timestamp"),
                BatteryLevel = row.GetValue<decimal>("battery_level")
            };

            if (!readingsBySensor.ContainsKey(reading.SensorId))
            {
                readingsBySensor[reading.SensorId] = new List<SensorReading>();
            }

            readingsBySensor[reading.SensorId].Add(reading);
        }

        // Process readings by sensor
        foreach (var sensorGroup in readingsBySensor)
        {
            ProcessSensorReadings(sensorGroup.Key, sensorGroup.Value);
        }
    }

    private void ProcessSensorReadings(Guid sensorId, List<SensorReading> readings)
    {
        // Calculate averages, detect anomalies, etc.
        var avgTemp = readings.Average(r => r.Temperature);
        var avgHumidity = readings.Average(r => r.Humidity);

        Console.WriteLine($"Sensor {sensorId}: Avg Temp={avgTemp:F1}°C, Avg Humidity={avgHumidity:F0}%");
    }
}
```

### Financial Transaction Processing

```csharp
public class TransactionProcessor
{
    private readonly Database _db;

    public void ProcessDailyTransactions()
    {
        var today = DateTime.UtcNow.Date;
        var results = _db.SelectStruct($"SELECT * FROM transactions WHERE date >= '{today:yyyy-MM-dd}'");

        decimal totalVolume = 0;
        var transactionsByType = new Dictionary<string, List<Transaction>>();

        foreach (var row in results)
        {
            var transaction = new Transaction
            {
                Id = row.GetValue<Guid>("id"),
                Amount = row.GetValue<decimal>("amount"),
                Type = row.GetValue<string>("type"),
                AccountId = row.GetValue<Guid>("account_id"),
                Timestamp = row.GetValue<DateTime>("timestamp"),
                Description = row.IsNull("description") ? "" : row.GetValue<string>("description")
            };

            totalVolume += Math.Abs(transaction.Amount);

            if (!transactionsByType.ContainsKey(transaction.Type))
            {
                transactionsByType[transaction.Type] = new List<Transaction>();
            }

            transactionsByType[transaction.Type].Add(transaction);
        }

        // Generate daily report
        Console.WriteLine($"Daily Volume: ${totalVolume:N2}");
        foreach (var typeGroup in transactionsByType)
        {
            var count = typeGroup.Value.Count;
            var amount = typeGroup.Value.Sum(t => t.Amount);
            Console.WriteLine($"  {typeGroup.Key}: {count} transactions, ${amount:N2}");
        }
    }
}
```

## Performance Tips

1. **Use column indices** instead of names for maximum performance
2. **Enable caching** when accessing the same columns repeatedly
3. **Process results immediately** - don't store StructRow instances
4. **Use parallel processing** for CPU-intensive operations on large datasets
5. **Batch operations** to control memory usage
6. **Check for nulls** before accessing nullable columns
7. **Profile your queries** to identify bottlenecks

## Data Type Reference

| C# Type | Database Type | Example |
|---------|---------------|---------|
| `int` | INTEGER | `row.GetValue<int>("count")` |
| `long` | LONG | `row.GetValue<long>("big_number")` |
| `double` | REAL | `row.GetValue<double>("price")` |
| `bool` | BOOLEAN | `row.GetValue<bool>("active")` |
| `string` | STRING | `row.GetValue<string>("name")` |
| `DateTime` | DATETIME | `row.GetValue<DateTime>("created")` |
| `decimal` | DECIMAL | `row.GetValue<decimal>("amount")` |
| `Guid` | GUID | `row.GetValue<Guid>("id")` |
| `Ulid` | ULID | `row.GetValue<Ulid>("ulid")` |
| `byte[]` | BLOB | `row.GetValue<byte[]>("data")` |

## Best Practices

- **Prefer column indices** over names in performance-critical code
- **Enable caching** for repeated column access patterns
- **Handle nulls explicitly** using `IsNull()` method
- **Process results in a streaming fashion** to minimize memory usage
- **Use parallel processing** for CPU-bound operations
- **Validate data types** at development time
- **Profile performance** regularly to identify optimization opportunities

This comprehensive guide should help you effectively use the StructRow API for high-performance data processing in your SharpCoreDB applications.
