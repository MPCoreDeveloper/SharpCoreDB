# Memory-Mapped Files Usage Examples

## Basic Usage

### Default Configuration (Recommended)
Memory mapping is enabled by default and works automatically:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;

var services = new ServiceCollection();
services.AddSharpCoreDB();
var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<DatabaseFactory>();

// Memory mapping is automatically used for large files (>10 MB)
var db = factory.Create("./mydb", "password");

db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, email TEXT)");
db.ExecuteSQL("INSERT INTO users VALUES ('1', 'John Doe', 'john@example.com')");

// This SELECT will use memory-mapped files if the table file is large enough
var results = db.ExecuteSQL("SELECT * FROM users WHERE name = 'John Doe'");
```

## Custom Configuration

### High-Performance Setup
For maximum performance with large databases:

```csharp
var config = new DatabaseConfig 
{ 
    NoEncryptMode = true,              // Disable encryption for speed
    UseMemoryMapping = true,           // Enable memory mapping
    MemoryMappingThreshold = 5L * 1024 * 1024,  // 5 MB threshold
    EnableQueryCache = true,           // Cache frequent queries
    EnableHashIndexes = true,          // Fast WHERE lookups
    WalBufferSize = 2 * 1024 * 1024   // 2 MB WAL buffer
};

var db = factory.Create("./mydb", "password", false, config);

// Large SELECT operations will benefit from memory mapping
db.ExecuteSQL("SELECT * FROM large_table WHERE category = 'Electronics'");
```

### Disable Memory Mapping
If you want to disable memory mapping:

```csharp
var config = new DatabaseConfig 
{ 
    UseMemoryMapping = false  // Disable memory mapping
};

var db = factory.Create("./mydb", "password", false, config);
```

### Custom Threshold
Set a custom threshold for when to use memory mapping:

```csharp
var config = new DatabaseConfig 
{ 
    UseMemoryMapping = true,
    MemoryMappingThreshold = 100L * 1024 * 1024  // 100 MB
};

var db = factory.Create("./mydb", "password", false, config);

// Only files larger than 100 MB will use memory mapping
```

## Advanced Usage

### Time-Tracking Application
Example with realistic time-tracking data:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using System.Diagnostics;

var services = new ServiceCollection();
services.AddSharpCoreDB();
var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<DatabaseFactory>();

// Configure for performance with memory mapping
var config = new DatabaseConfig 
{ 
    NoEncryptMode = true,
    UseMemoryMapping = true,
    MemoryMappingThreshold = 10L * 1024 * 1024,
    EnableQueryCache = true
};

var db = factory.Create("./timetrack.db", "securePassword", false, config);

// Create table
db.ExecuteSQL(@"
    CREATE TABLE time_entries (
        id INTEGER PRIMARY KEY, 
        project TEXT, 
        task TEXT, 
        start_time DATETIME, 
        end_time DATETIME,
        duration INTEGER, 
        user TEXT, 
        description TEXT
    )
");

// Insert large dataset
Console.WriteLine("Inserting 10,000 time entries...");
var sw = Stopwatch.StartNew();
for (int i = 0; i < 10_000; i++)
{
    db.ExecuteSQL($@"
        INSERT INTO time_entries VALUES (
            '{i}', 
            'Project{i % 100}', 
            'Task{i % 20}', 
            '2024-01-{(i % 28) + 1:00} 09:00:00',
            '2024-01-{(i % 28) + 1:00} 17:00:00',
            '480', 
            'User{i % 10}', 
            'Description for task {i}'
        )
    ");
}
sw.Stop();
Console.WriteLine($"Insert completed in {sw.ElapsedMilliseconds}ms");

// Query with memory mapping (fast!)
sw.Restart();
var entries = db.ExecuteSQL("SELECT * FROM time_entries WHERE project = 'Project50'");
sw.Stop();
Console.WriteLine($"Query completed in {sw.ElapsedMilliseconds}ms");
Console.WriteLine($"Found {entries?.Count ?? 0} entries");

// Complex aggregation query
sw.Restart();
var summary = db.ExecuteSQL(@"
    SELECT user, COUNT(*) as entry_count 
    FROM time_entries 
    WHERE project LIKE 'Project%' 
    GROUP BY user
");
sw.Stop();
Console.WriteLine($"Aggregation completed in {sw.ElapsedMilliseconds}ms");
```

### E-Commerce Product Database
Example with product catalog:

```csharp
var config = DatabaseConfig.HighPerformance; // Pre-configured for performance

var db = factory.Create("./products.db", "password", false, config);

db.ExecuteSQL(@"
    CREATE TABLE products (
        id INTEGER PRIMARY KEY,
        sku TEXT,
        name TEXT,
        description TEXT,
        price REAL,
        category TEXT,
        stock INTEGER
    )
");

// Insert sample products
for (int i = 0; i < 50_000; i++)
{
    db.ExecuteSQL($@"
        INSERT INTO products VALUES (
            '{i}',
            'SKU-{i:D6}',
            'Product {i}',
            'Detailed description for product {i}...',
            '{(i % 1000) + 9.99}',
            'Category{i % 20}',
            '{i % 100}'
        )
    ");
}

// Fast product search with memory mapping
var results = db.ExecuteSQL("SELECT * FROM products WHERE category = 'Category5' AND price < 500");
Console.WriteLine($"Found {results?.Count ?? 0} products");
```

## Monitoring and Debugging

### Check if Memory Mapping is Active

```csharp
var config = new DatabaseConfig 
{ 
    UseMemoryMapping = true,
    MemoryMappingThreshold = 10L * 1024 * 1024
};

Console.WriteLine($"Memory Mapping Enabled: {config.UseMemoryMapping}");
Console.WriteLine($"Threshold: {config.MemoryMappingThreshold / (1024.0 * 1024.0):F2} MB");

var db = factory.Create("./mydb", "password", false, config);

// Check file sizes
var dbPath = "./mydb";
var dataPath = Path.Combine(dbPath, "data");
if (Directory.Exists(dataPath))
{
    foreach (var file in Directory.GetFiles(dataPath, "*.bin"))
    {
        var fileInfo = new FileInfo(file);
        var willUseMmap = fileInfo.Length >= config.MemoryMappingThreshold;
        Console.WriteLine($"{Path.GetFileName(file)}: {fileInfo.Length / (1024.0 * 1024.0):F2} MB - MMF: {willUseMmap}");
    }
}
```

### Performance Comparison

```csharp
using System.Diagnostics;

// Test without memory mapping
var configNoMmap = new DatabaseConfig { UseMemoryMapping = false };
var dbNoMmap = factory.Create("./test_nommap", "password", false, configNoMmap);
dbNoMmap.ExecuteSQL("CREATE TABLE test (id INTEGER PRIMARY KEY, data TEXT)");
for (int i = 0; i < 10_000; i++)
    dbNoMmap.ExecuteSQL($"INSERT INTO test VALUES ('{i}', 'Data {i}')");

var swNoMmap = Stopwatch.StartNew();
for (int i = 0; i < 100; i++)
    dbNoMmap.ExecuteSQL("SELECT * FROM test WHERE id = '5000'");
swNoMmap.Stop();
Console.WriteLine($"Without MMF: {swNoMmap.ElapsedMilliseconds}ms");

// Test with memory mapping
var configMmap = new DatabaseConfig { UseMemoryMapping = true };
var dbMmap = factory.Create("./test_mmap", "password", false, configMmap);
dbMmap.ExecuteSQL("CREATE TABLE test (id INTEGER PRIMARY KEY, data TEXT)");
for (int i = 0; i < 10_000; i++)
    dbMmap.ExecuteSQL($"INSERT INTO test VALUES ('{i}', 'Data {i}')");

var swMmap = Stopwatch.StartNew();
for (int i = 0; i < 100; i++)
    dbMmap.ExecuteSQL("SELECT * FROM test WHERE id = '5000'");
swMmap.Stop();
Console.WriteLine($"With MMF: {swMmap.ElapsedMilliseconds}ms");

var improvement = ((swNoMmap.ElapsedMilliseconds - swMmap.ElapsedMilliseconds) / (double)swNoMmap.ElapsedMilliseconds) * 100;
Console.WriteLine($"Performance improvement: {improvement:F1}%");
```

## Best Practices

### 1. Use Default Configuration for Most Cases
```csharp
// This is usually sufficient
var db = factory.Create("./mydb", "password");
```

### 2. Enable Memory Mapping for Large Databases
```csharp
// For databases with files > 10 MB
var config = new DatabaseConfig { UseMemoryMapping = true };
var db = factory.Create("./mydb", "password", false, config);
```

### 3. Adjust Threshold Based on Data Size
```csharp
// For very large databases (> 100 MB)
var config = new DatabaseConfig 
{ 
    UseMemoryMapping = true,
    MemoryMappingThreshold = 50L * 1024 * 1024  // 50 MB
};
var db = factory.Create("./mydb", "password", false, config);
```

### 4. Combine with Other Optimizations
```csharp
// Maximum performance
var config = new DatabaseConfig 
{ 
    NoEncryptMode = true,           // If security allows
    UseMemoryMapping = true,
    EnableQueryCache = true,
    EnableHashIndexes = true,
    UseBufferedIO = true
};
var db = factory.Create("./mydb", "password", false, config);
```

### 5. Disable for Small Databases
```csharp
// For databases that stay small (< 10 MB)
var config = new DatabaseConfig { UseMemoryMapping = false };
var db = factory.Create("./mydb", "password", false, config);
```

## Troubleshooting

### Memory Mapping Not Working?

Check:
1. File size exceeds threshold (default 10 MB)
2. `UseMemoryMapping` is `true` in config
3. Operating system supports memory-mapped files
4. Sufficient virtual memory available

### Performance Not Improved?

Possible causes:
1. Files are too small (< 10 MB)
2. Database is already in OS cache
3. Disk is very fast (SSD with good cache)
4. Workload is write-heavy (MMF only helps reads)

### Out of Memory Errors?

Solutions:
1. Increase `MemoryMappingThreshold`
2. Disable memory mapping for some tables
3. Close database connections when not needed
4. Use 64-bit process for large files
