# SharpCoreDB Examples

This document provides practical examples for using SharpCoreDB in time-tracking, invoicing, and project management applications.

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
// Start a time entry
db.ExecuteSQL(@"INSERT INTO time_entries (project_id, user_id, task, start_time, billable, notes) 
                VALUES ('1', '101', 'Feature Development', NOW(), 'true', 'Working on user authentication')");

// Stop a time entry and calculate duration
db.ExecuteSQL(@"UPDATE time_entries 
                SET end_time = NOW(), 
                    duration = DATEDIFF(NOW(), start_time, 'minutes')
                WHERE id = 'entry_id_here'");
```

### Querying Time Data

```csharp
// Get today's time entries
db.ExecuteSQL(@"SELECT * FROM time_entries 
                WHERE DATE(start_time) = DATE(NOW())");

// Get time entries for a specific project this week
db.ExecuteSQL(@"SELECT * FROM time_entries 
                WHERE project_id = '1' 
                AND start_time >= DATEADD(NOW(), -7, 'days')");

// Calculate total hours by project this month
db.ExecuteSQL(@"SELECT project_id, SUM(duration) as total_minutes 
                FROM time_entries 
                WHERE start_time >= DATEADD(NOW(), -30, 'days')
                GROUP BY project_id");

// Get billable vs non-billable hours
db.ExecuteSQL(@"SELECT billable, SUM(duration) as total_minutes 
                FROM time_entries 
                GROUP BY billable");
```

### Reporting Queries

```csharp
// Weekly report by user
db.ExecuteSQL(@"SELECT 
    users.full_name,
    COUNT(*) as entry_count,
    SUM(duration) as total_minutes,
    AVG(duration) as avg_minutes
FROM time_entries
JOIN users ON time_entries.user_id = users.id
WHERE start_time >= DATEADD(NOW(), -7, 'days')
GROUP BY users.full_name");

// Project profitability report
db.ExecuteSQL(@"SELECT 
    projects.name,
    projects.hourly_rate,
    SUM(time_entries.duration) / 60.0 as total_hours,
    (SUM(time_entries.duration) / 60.0) * projects.hourly_rate as total_revenue
FROM time_entries
JOIN projects ON time_entries.project_id = projects.id
WHERE time_entries.billable = 'true'
GROUP BY projects.name, projects.hourly_rate");
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

// Example API endpoint
app.MapPost("/api/time-entries", async (DatabasePool pool, TimeEntryRequest request) =>
{
    var db = pool.GetDatabase("timetracker.db", "secure_password");
    
    try
    {
        db.ExecuteSQL($@"INSERT INTO time_entries 
            (project_id, user_id, task, start_time, billable) 
            VALUES ('{request.ProjectId}', '{request.UserId}', '{request.Task}', 
                    '{request.StartTime}', '{request.Billable}')");
        
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

## Using Auto Maintenance

```csharp
using SharpCoreDB.Services;

// Set up automatic database maintenance
var db = factory.Create("timetracker.db", "password");
using var maintenance = new AutoMaintenanceService(
    db,
    intervalSeconds: 300,    // Run maintenance every 5 minutes
    writeThreshold: 1000     // Or after 1000 write operations
);

// Track writes in your application
void LogTimeEntry(string sql)
{
    db.ExecuteSQL(sql);
    maintenance.IncrementWriteCount();
}

// Maintenance happens automatically in the background
// Manual trigger if needed
maintenance.TriggerMaintenance();
```

## Dapper Integration Example

```csharp
using SharpCoreDB.Extensions;
using Dapper;

// Define models
public class Project
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Client { get; set; }
    public decimal HourlyRate { get; set; }
    public DateTime Created { get; set; }
}

public class TimeEntry
{
    public string Id { get; set; }
    public int ProjectId { get; set; }
    public int UserId { get; set; }
    public string Task { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int Duration { get; set; }
    public bool Billable { get; set; }
}

// Use Dapper for queries
var connection = db.GetDapperConnection();

// Query with Dapper
var projects = connection.Query<Project>("SELECT * FROM projects");

// Parameterized queries (basic implementation)
// Note: Full parameter support requires extending the implementation
var entries = connection.Query<TimeEntry>(
    "SELECT * FROM time_entries WHERE project_id = '1'");
```

## Health Monitoring Setup

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add database with health checks
builder.Services.AddSharpCoreDB();
builder.Services.AddHealthChecks()
    .AddSharpCoreDB(
        db, 
        name: "database",
        testQuery: "SELECT COUNT(*) FROM users",
        tags: new[] { "db", "ready" }
    );

var app = builder.Build();

// Health check endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();
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

// Generate invoice from time entries
db.ExecuteSQL(@"INSERT INTO invoices (id, client, invoice_date, due_date, status)
                VALUES ('1001', 'Acme Corp', NOW(), DATEADD(NOW(), 30, 'days'), 'draft')");

// Add invoice items from billable time entries
db.ExecuteSQL(@"INSERT INTO invoice_items (invoice_id, description, hours, rate, amount)
                SELECT 
                    '1001',
                    GROUP_CONCAT(task, ', '),
                    SUM(duration) / 60.0,
                    '150.00',
                    (SUM(duration) / 60.0) * 150.00
                FROM time_entries
                WHERE project_id = '1' 
                AND billable = 'true'
                AND start_time >= '2024-01-01'
                AND start_time < '2024-02-01'");

// Calculate invoice total
db.ExecuteSQL(@"UPDATE invoices
                SET total_amount = (
                    SELECT SUM(amount) 
                    FROM invoice_items 
                    WHERE invoice_id = '1001'
                )
                WHERE id = '1001'");
```

## Performance Tips

### 1. Use Indexes for Frequently Queried Columns

```csharp
// Create indexes on columns used in WHERE clauses
db.ExecuteSQL("CREATE INDEX idx_entries_date ON time_entries (start_time)");
db.ExecuteSQL("CREATE INDEX idx_entries_user ON time_entries (user_id)");

// Check if index is being used
db.ExecuteSQL("EXPLAIN SELECT * FROM time_entries WHERE start_time > '2024-01-01'");
```

### 2. Use Connection Pooling for Web Applications

```csharp
// Initialize pool once at startup
var pool = new DatabasePool(services, maxPoolSize: 50);

// Reuse connections
var db = pool.GetDatabase(dbPath, password);
// ... use database ...
pool.ReturnDatabase(db);
```

### 3. Batch Operations

```csharp
// Instead of multiple individual inserts
for (int i = 0; i < 1000; i++)
{
    db.ExecuteSQL($"INSERT INTO logs VALUES ('{i}', 'message')");
}

// Consider batching or using UPSERT
db.ExecuteSQL("INSERT OR REPLACE INTO logs VALUES ('1', 'message')");
```

### 4. Regular Maintenance

```csharp
// Set up automatic maintenance
var maintenance = new AutoMaintenanceService(
    db,
    intervalSeconds: 600,     // 10 minutes
    writeThreshold: 5000      // Or 5000 writes
);

// This keeps the database optimized automatically
```

## Connection String Examples

```csharp
// Simple connection string
var connStr = "Data Source=myapp.db;Password=secret";

// Full options
var connStr = "Data Source=timetracker.sharpcoredb;Password=MySecret123;ReadOnly=False;Cache=Shared";

// Parse and modify
var builder = new ConnectionStringBuilder(connStr);
builder.ReadOnly = true;
var readOnlyConnStr = builder.BuildConnectionString();
```

## Error Handling

```csharp
try
{
    db.ExecuteSQL("INSERT INTO users VALUES ('1', 'Alice')");
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"Database error: {ex.Message}");
}

// Read-only mode protection
try
{
    var readOnlyDb = factory.Create(dbPath, password, isReadOnly: true);
    readOnlyDb.ExecuteSQL("INSERT INTO users VALUES ('2', 'Bob')");
}
catch (InvalidOperationException ex)
{
    Console.WriteLine("Cannot modify database in read-only mode");
}
```

## Best Practices

1. **Always use connection pooling** for web applications to manage resources efficiently
2. **Create indexes** on columns frequently used in WHERE clauses and JOINs
3. **Enable auto maintenance** to keep the database optimized
4. **Use read-only mode** when you only need to query data
5. **Implement health checks** to monitor database connectivity
6. **Use UPSERT** instead of separate INSERT/UPDATE logic
7. **Leverage date/time functions** for time-based queries
8. **Use aggregate functions** to calculate summaries efficiently
9. **Regular backups** of your database files
10. **Monitor pool statistics** to tune pool size appropriately
