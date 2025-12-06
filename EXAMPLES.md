# SharpCoreDB Examples

This document provides practical examples for using SharpCoreDB in time-tracking, invoicing, and project management applications.

## ⚠️ Security Notice

**Important:** SharpCoreDB now supports parameterized queries for security. Always use parameterized queries in production:

1. **Use parameterized queries** with `?` placeholders
2. **Pass parameters as Dictionary** to ExecuteSQL methods
3. **Validate input types, lengths, and formats**
4. **Never concatenate user input** directly into SQL strings

## Time Tracking Application Example

### Schema Setup

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Services;

// Initialize database
var services = new ServiceCollection();
services.AddSharpCoreDB();
var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<DatabaseFactory>();

var db = factory.Create("timetracker.db", "secure_password");

// Create tables for time tracking
db.ExecuteSQL(@"CREATE TABLE projects (
    id INTEGER PRIMARY KEY, 
    name TEXT, 
    client TEXT, 
    hourly_rate DECIMAL, 
    created DATETIME
)");

db.ExecuteSQL(@"CREATE TABLE time_entries (
    id ULID PRIMARY KEY AUTO, 
    project_id INTEGER, 
    user_id INTEGER,
    task TEXT, 
    start_time DATETIME, 
    end_time DATETIME, 
    duration INTEGER,
    billable BOOLEAN,
    notes TEXT
)");

db.ExecuteSQL(@"CREATE TABLE users (
    id INTEGER PRIMARY KEY, 
    username TEXT, 
    email TEXT, 
    full_name TEXT,
    created DATETIME
)");

// Create indexes for better query performance
db.ExecuteSQL("CREATE INDEX idx_time_entries_project ON time_entries (project_id)");
db.ExecuteSQL("CREATE INDEX idx_time_entries_user ON time_entries (user_id)");
db.ExecuteSQL("CREATE INDEX idx_time_entries_start ON time_entries (start_time)");
```

### Recording Time Entries

```csharp
// Start a time entry - using parameterized query
db.ExecuteSQL(@"INSERT INTO time_entries (project_id, user_id, task, start_time, billable, notes) 
                VALUES (?, ?, ?, ?, ?, ?)", 
    new Dictionary<string, object?> {
        { "0", 1 },
        { "1", 101 },
        { "2", "Feature Development" },
        { "3", DateTime.Now },
        { "4", true },
        { "5", "Working on user authentication" }
    });

// Stop a time entry and calculate duration
var entryId = "some_ulid_here";
var endTime = DateTime.Now;
db.ExecuteSQL(@"UPDATE time_entries 
                SET end_time = ?, 
                    duration = ?
                WHERE id = ?",
    new Dictionary<string, object?> {
        { "0", endTime },
        { "1", CalculateDurationMinutes(startTime, endTime) },
        { "2", entryId }
    });
```

### Querying Time Data

```csharp
// Get today's time entries
db.ExecuteSQL(@"SELECT * FROM time_entries 
                WHERE DATE(start_time) = DATE(NOW())");

// Get time entries for a specific project this week
var projectId = 1;
db.ExecuteSQL(@"SELECT * FROM time_entries 
                WHERE project_id = ? 
                AND start_time >= DATEADD(NOW(), -7, 'days')",
    new Dictionary<string, object?> { { "0", projectId } });

// Calculate total hours by project this month
db.ExecuteSQL(@"SELECT project_id, SUM(duration) as total_minutes 
                FROM time_entries 
                WHERE start_time >= DATEADD(NOW(), -30, 'days')
                GROUP BY project_id");
```

## Using Connection Pooling for Web Applications

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Services;

var builder = WebApplication.CreateBuilder(args);

// Register SharpCoreDB with connection pooling
builder.Services.AddSharpCoreDB();
builder.Services.AddSingleton<DatabasePool>(sp => 
    new DatabasePool(sp, maxPoolSize: 20));

var app = builder.Build();

// Example API endpoint with proper parameterized queries
app.MapPost("/api/time-entries", async (DatabasePool pool, TimeEntryRequest request) =>
{
    var db = pool.GetDatabase("timetracker.db", "secure_password");
    
    try
    {
        // Validate input
        if (request.ProjectId <= 0 || request.UserId <= 0)
            return Results.BadRequest("Invalid project or user ID");
        
        if (string.IsNullOrWhiteSpace(request.Task))
            return Results.BadRequest("Task description is required");
        
        if (request.Task.Length > 500)
            return Results.BadRequest("Task description too long");
        
        // Use parameterized query - SAFE from SQL injection
        db.ExecuteSQL(@"INSERT INTO time_entries 
            (project_id, user_id, task, start_time, billable) 
            VALUES (?, ?, ?, ?, ?)",
            new Dictionary<string, object?> {
                { "0", request.ProjectId },
                { "1", request.UserId },
                { "2", request.Task },
                { "3", request.StartTime },
                { "4", request.Billable }
            });
        
        return Results.Ok(new { success = true });
    }
    finally
    {
        pool.ReturnDatabase(db);
    }
});

app.Run();

public record TimeEntryRequest(int ProjectId, int UserId, string Task, DateTime StartTime, bool Billable);
```

## Console Application with Dependency Injection

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

class Program
{
    static void Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<DatabaseFactory>();
        var db = factory.Create("console.db", "secure_password");

        // Create table
        db.ExecuteSQL("CREATE TABLE messages (id INTEGER PRIMARY KEY, content TEXT, timestamp DATETIME)");

        // Insert data with parameterized query
        db.ExecuteSQL("INSERT INTO messages VALUES (?, ?, ?)", 
            new Dictionary<string, object?> {
                { "0", 1 },
                { "1", "Hello from SharpCoreDB!" },
                { "2", DateTime.Now }
            });

        // Query data
        db.ExecuteSQL("SELECT * FROM messages");

        // Async operations
        Task.Run(async () =>
        {
            await db.ExecuteSQLAsync("INSERT INTO messages VALUES (?, ?, ?)", 
                new Dictionary<string, object?> {
                    { "0", 2 },
                    { "1", "Async message" },
                    { "2", DateTime.Now }
                });
            
            await db.ExecuteSQLAsync("SELECT COUNT(*) FROM messages");
        }).Wait();

        Console.WriteLine("Console app completed successfully!");
    }
}
```

## Batch Operations Example

```csharp
// Create database
var db = factory.Create("batch.db", "password");
db.ExecuteSQL("CREATE TABLE bulk_data (id INTEGER, value TEXT, timestamp DATETIME)"));

// Use parameterized queries for safety - even in batch operations
for (int i = 0; i < 1000; i++)
{
    db.ExecuteSQL("INSERT INTO bulk_data VALUES (?, ?, ?)",
        new Dictionary<string, object?> {
            { "0", i },
            { "1", $"Value_{i}" },
            { "2", DateTime.Now }
        });
}
```

## Invoice Generation Example

```csharp
// Create invoices table
db.ExecuteSQL(@"CREATE TABLE invoices (
    id INTEGER PRIMARY KEY,
    client TEXT,
    invoice_date DATETIME,
    due_date DATETIME,
    total_amount DECIMAL,
    status TEXT
)");

db.ExecuteSQL(@"CREATE TABLE invoice_items (
    id INTEGER PRIMARY KEY,
    invoice_id INTEGER,
    description TEXT,
    hours DECIMAL,
    rate DECIMAL,
    amount DECIMAL
)");

// Generate invoice with parameterized queries
var invoiceId = 1001;
var clientName = "Acme Corp";
var dueDate = DateTime.Now.AddDays(30);

db.ExecuteSQL(@"INSERT INTO invoices (id, client, invoice_date, due_date, status)
                VALUES (?, ?, ?, ?, ?)",
    new Dictionary<string, object?> {
        { "0", invoiceId },
        { "1", clientName },
        { "2", DateTime.Now },
        { "3", dueDate },
        { "4", "draft" }
    });

// Add invoice items
db.ExecuteSQL(@"INSERT INTO invoice_items (invoice_id, description, hours, rate, amount)
                VALUES (?, ?, ?, ?, ?)",
    new Dictionary<string, object?> {
        { "0", invoiceId },
        { "1", "Development Services" },
        { "2", 40.0m },
        { "3", 150.00m },
        { "4", 6000.00m }
    });
```

## Performance Configuration

```csharp
// High-performance mode (no encryption, trusted environments)
var config = DatabaseConfig.HighPerformance;
var db = factory.Create(dbPath, password, false, config);

// Custom configuration
var customConfig = new DatabaseConfig 
{ 
    EnableQueryCache = true,
    QueryCacheSize = 1000,
    EnableHashIndexes = true,
    WalBufferSize = 1024 * 1024,
    UseBufferedIO = true
};
var customDb = factory.Create(dbPath, password, false, customConfig);
```

## Hash Indexes for Fast Queries

```csharp
// Create table and index
db.ExecuteSQL("CREATE TABLE time_entries (id INTEGER, project TEXT, duration INTEGER)");
db.ExecuteSQL("CREATE INDEX idx_project ON time_entries (project)");

// Insert data with parameterized queries
for (int i = 0; i < 1000; i++)
{
    db.ExecuteSQL("INSERT INTO time_entries VALUES (?, ?, ?)",
        new Dictionary<string, object?> {
            { "0", i },
            { "1", $"Project_{i % 10}" },
            { "2", i * 60 }
        });
}

// Query uses hash index automatically - 5-10x faster!
db.ExecuteSQL("SELECT * FROM time_entries WHERE project = ?",
    new Dictionary<string, object?> { { "0", "Project_5" } });
```

## Best Practices

### ✅ DO: Use Parameterized Queries

```csharp
// GOOD - Safe from SQL injection
var userId = GetUserInput();
db.ExecuteSQL("SELECT * FROM users WHERE id = ?",
    new Dictionary<string, object?> { { "0", userId } });
```

### ❌ DON'T: Concatenate User Input

```csharp
// BAD - Vulnerable to SQL injection!
var userId = GetUserInput();
db.ExecuteSQL($"SELECT * FROM users WHERE id = '{userId}'");
```

### ✅ DO: Validate Input First

```csharp
// Validate before using
if (userId <= 0 || userId > int.MaxValue)
    throw new ArgumentException("Invalid user ID");

db.ExecuteSQL("INSERT INTO logs VALUES (?, ?)",
    new Dictionary<string, object?> {
        { "0", userId },
        { "1", DateTime.Now }
    });
```

### ✅ DO: Use Async for Web Apps

```csharp
// Non-blocking operations
await db.ExecuteSQLAsync("INSERT INTO users VALUES (?, ?)",
    new Dictionary<string, object?> {
        { "0", 1 },
        { "1", "Alice" }
    });
```

---

**Note**: All examples use parameterized queries to prevent SQL injection vulnerabilities. This is the recommended approach for all production applications.
